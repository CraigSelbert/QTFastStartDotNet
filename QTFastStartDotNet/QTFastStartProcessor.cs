using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;

namespace QTFastStartDotNet
{
    public class QTFastStartProcessor
    {

        public QTFastStartProcessor() { }

        private const int CHUNK_SIZE = 8192;

        public IEnumerable<Atom> GetIndex(Stream r)
        {
            Debug.Print("Getting index of top level atoms...");

            var index = ReadAtoms(r).ToList();
            EnsureValidIndex(index);

            return index;
        }

        private IEnumerable<Atom> ReadAtoms(Stream r)
        {
            while (r.CanRead)
            {
                Atom a;
                try
                {
                    a = ReadAtomEx(r);
                }
                catch (Exception ex)
                {

                    break;
                }
                Debug.Print("{0}: {1}", a.Name, a.Size);

                yield return a;

                if (a.Size == 0)
                {
                    if (a.Name == "mdat")
                    {
                        //Some files may end in mdat with no size set, which generally
                        //means to seek to the end of the file. We can just stop indexing
                        //as no more entries will be found!
                        break;
                    }
                    else
                    {
                        //Weird, but just continue to try to find more atoms
                        continue;
                    }
                }

                r.Seek(a.Position + a.Size, SeekOrigin.Begin);
            }
        }

        private Atom ReadAtomEx(Stream s)
        {
            var pos = s.Position;

            var atom = ReadAtom(s);

            var size = atom.Item1;
            if (atom.Item1 == 1)
            {
                size = BitConverter.ToUInt64(ReadBytes(s, 8), 0);  //based >Q in python code.  Q is unsigned long long.  
            }

            return new Atom
            {
                Name = atom.Item2,
                Position = pos,
                Size = (int)size
            };
        }

        private Tuple<ulong, string> ReadAtom(Stream s)
        {
            var i = UnpackULong(ReadBytes(s, 4));
            var n = UnpackString(ReadBytes(s, 4), 4);

            return new Tuple<ulong, string>(i, n);
        }

        private void EnsureValidIndex(IEnumerable<Atom> atoms)
        {
            var topLevelAtoms = new HashSet<string>(atoms.Select(x => x.Name));
            var required = new List<string> { "moov", "mdat" };

            foreach (var r in required)
            {
                if (!topLevelAtoms.Contains(r))
                {
                    var msg = string.Format("{0} atom not found, is this a valid MOV/MP4 file?", r);
                    Debug.Print(msg);
                    throw new InvalidFormatException(msg);
                }
            }
        }

        public void Process(string inputFileName, string outputFileName, float limit = float.PositiveInfinity,
            bool toEnd = false, bool cleanup = true)
        {
            using (var inputStream = new FileStream(inputFileName, FileMode.Open))
            using (var outputStream = new FileStream(outputFileName, FileMode.Create))
            {
                Process(inputStream, outputStream, limit, toEnd, cleanup);
            }

            try
            {
                File.SetAccessControl(outputFileName, File.GetAccessControl(inputFileName));
            }
            catch
            {
                Debug.Print(string.Format("Could not copy file permissions!"));
            }
        }

        public QTFastStartProcessingStatus Process(Stream datastream, Stream outputStream, float limit = float.PositiveInfinity,
            bool toEnd = false, bool cleanup = true)
        {
            //Get the top level atom index
            IEnumerable<Atom> index;
            try
            {
                index = GetIndex(datastream);
            }
            catch (InvalidFormatException)
            {
                return QTFastStartProcessingStatus.InvalidFormat;
            }

            long mdatPosition = 999999;
            long freeSize = 0;

            Atom moovAtom = new Atom {Name = "", Position = 0, Size = 0};

            //Make sure moov occurs AFTER mdat, otherwise no need to run!
            foreach (var atom in index)
            {
                //The atoms are guaranteed to exist from get_index above!
                if (atom.Name == "moov")
                {
                    moovAtom = atom;
                }
                else if (atom.Name == "mdat")
                {
                    mdatPosition = atom.Position;
                }
                else if (atom.Name == "free" && atom.Position < mdatPosition && cleanup)
                {
                    //This free atom is before the mdat!
                    freeSize += atom.Size;

                    Debug.Print(string.Format("Removing free atom at {0} ({1} bytes)", atom.Position, atom.Size));
                }
                else if (atom.Name == "\x00\x00\x00\x00" && atom.Position < mdatPosition)
                {
                    //This is some strange zero atom with incorrect size
                    freeSize += 8;
                    Debug.Print(string.Format("Removing strange zero atom at {0} (8 bytes)", atom.Position));
                }
            }


            //Offset to shift positions
            long offset = -1*freeSize;

            if (moovAtom.Position < mdatPosition)
            {
                if (toEnd)
                {
                    //moov is in the wrong place, shift by moov size
                    offset -= moovAtom.Size;
                }
            }
            else
            {
                if (!toEnd)
                {
                    //moov is in the wrong place, shift by moov size
                    offset += moovAtom.Size;
                }
            }


            if (offset == 0)
            {
                //No free atoms to process and moov is correct, we are done!
                var msg = "This file appears to already be setup!";
                Debug.Print(msg);
                return QTFastStartProcessingStatus.AlreadyConverted;
            }

            //Check for compressed moov atom
            if (IsMoovCompressed(datastream, moovAtom))
            {
                var msg = "Movies with compressed headers are not supported";
                Debug.Print(msg);
                return QTFastStartProcessingStatus.FileIsCompressed;
            }

            //read and fix moov
            var moov = PatchMoov(datastream, moovAtom, offset);

            Debug.Print("Writing output...");
            //Write ftype
            foreach (var atom in index)
            {
                if (atom.Name == "ftyp")
                {
                    Debug.Print(string.Format("Writing ftyp... ({0} bytes)", atom.Size));
                    byte[] b = new byte[atom.Size];
                    datastream.Seek(atom.Position, SeekOrigin.Begin);
                    var read = datastream.Read(b, 0, atom.Size);
                    outputStream.Write(b, 0, read);
                }
            }

            if (!toEnd)
            {
                WriteMoov(moov, outputStream);
            }
            //Write the rest
            var skipAtomTypes = new HashSet<string>() {"ftyp", "moov"};

            if (cleanup)
            {
                skipAtomTypes.Add("free");
            }

            var atoms = index.Where(x => !skipAtomTypes.Contains(x.Name));

            foreach (var atom in atoms)
            {
                Debug.Print(string.Format("Writing {0}... ({1} bytes)", atom.Name, atom.Size));
                datastream.Seek(atom.Position, SeekOrigin.Begin);

                //for compatability, allow '0' to mean no limit
                var curLimit = limit > 0 ? limit : float.PositiveInfinity;
                curLimit = Math.Min(curLimit, atom.Size);

                foreach (var chunk in GetChunks(datastream, CHUNK_SIZE, curLimit))
                {
                    outputStream.Write(chunk, 0, chunk.Length);
                }
            }
            if (toEnd)
            {
                WriteMoov(moov, outputStream);
            }

            return QTFastStartProcessingStatus.Success;
        }


        private void WriteMoov(MemoryStream moov, Stream outfile)
        {
            moov.Seek(0, SeekOrigin.Begin);
            byte[] bytes = new byte[1024];
            var read = 1;
            while (read > 0)
            {
                read = moov.Read(bytes, 0, 1024);
                outfile.Write(bytes, 0, read);
            }
            Debug.Print(string.Format("Writing moov... ({0} bytes)", bytes.Length));
        }

        private IEnumerable<Atom> FindAtomsEx(Atom parent, Stream s)
        {
            var ancestors = new HashSet<string> { "trak", "mdia", "minf", "stbl" };
            var stop = parent.Position + parent.Size;

            while (s.Position < stop)
            {
                Atom atom = new Atom();
                try
                {
                    atom = ReadAtomEx(s);
                }
                catch
                {
                    var msg = "Error reading next atom";
                    Debug.Print(msg);
                    throw new AtomReadingException(msg);
                }

                if (ancestors.Contains(atom.Name))
                {
                    foreach (var a in FindAtomsEx(atom, s))
                    {
                        yield return a;
                    }
                }
                else if (atom.Name == "stco" || atom.Name == "co64")
                {
                    yield return atom;
                }
                else
                {
                    s.Seek(atom.Position + atom.Size, SeekOrigin.Begin);
                }

            }
        }


        private MemoryStream PatchMoov(Stream datastream, Atom moovAtom, long offset)
        {
            datastream.Seek(moovAtom.Position, SeekOrigin.Begin);
            var moov = new MemoryStream(ReadBytes(datastream, moovAtom.Size));

            var atom = ReadAtomEx(moov);

            foreach (var a in FindAtomsEx(atom, moov))
            {
                string ctype;
                int csize;

                var version = UnpackULong(ReadBytes(moov, 4));
                var entryCount = UnpackULong(ReadBytes(moov, 4));

                Debug.Print(string.Format("Patching {0} with {1} entries", a.Name, entryCount));

                var entriesPos = moov.Position;

                var entries = UnpackEntries(moov, a.Name, entryCount).ToList();
                var offsetEntries = entries.Select(x => x + (ulong)offset).ToList();

                moov.Seek(entriesPos, SeekOrigin.Begin);
                EndianBinaryWriter w = new EndianBinaryWriter(EndianBitConverter.Big, moov);
                if (a.Name == "stco")
                {
                    offsetEntries.ForEach(x => w.Write((UInt32)x));
                }
                else if (a.Name == "co64")
                {
                    offsetEntries.ForEach(x => w.Write((UInt64)x));
                }
            }

            return moov;
        }

        private IEnumerable<ulong> UnpackEntries(Stream s, string type, uint numEntries)
        {
            for (int i = 0; i < numEntries; i++)
            {
                if (type == "co64")
                {
                    yield return UnpackULongLong(ReadBytes(s, 8));
                }
                if (type == "stco")
                {
                    yield return UnpackULong(ReadBytes(s, 4));
                }
            }
        }

        private bool IsMoovCompressed(Stream datastream, Atom moovAtom)
        {
            datastream.Seek(moovAtom.Position + 8, SeekOrigin.Begin);
            var stop = moovAtom.Position + moovAtom.Size;

            while (datastream.Position < stop)
            {
                var child = ReadAtomEx(datastream);
                datastream.Seek(datastream.Position + child.Size - 8, SeekOrigin.Begin);

                if (child.Name == "cmov")
                {
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<Byte[]> GetChunks(Stream stream, long chunkSize, float limit)
        {
            var remaining = limit;

            while (remaining > 0 && stream.CanRead)
            {
                var bytesToRead = (int)Math.Min(remaining, chunkSize);
                var bytes = new byte[bytesToRead];
                remaining -= stream.Read(bytes, 0, bytesToRead);

                yield return bytes;
            }
        }

        private static byte[] ReadBytes(Stream s, int numBytes)
        {
            var bytes = new byte[numBytes];

            var read = s.Read(bytes, 0, numBytes);
            if (read <= 0)
            {
                throw new Exception("End of File");
            }
            return bytes;
        }

        public static UInt32 UnpackULong(byte[] bytes, int offset = 0)
        {
            return EndianBitConverter.Big.ToUInt32(bytes, offset);
        }

        public static UInt64 UnpackULongLong(byte[] bytes, int offset = 0)
        {
            return EndianBitConverter.Big.ToUInt64(bytes, offset);
        }

        public static string UnpackString(byte[] bytes, int length, int offset = 0)
        {
            return Encoding.Default.GetString(bytes, offset, length);
        }
    }
}
