Quicktime/MP4 Fast Start
------------------------
Enable streaming and pseudo-streaming of Quicktime and MP4 files by
moving metadata and offset information to the front of the file.

This program is based on qt-faststart.c from the ffmpeg project, which is
released into the public domain, as well as ISO 14496-12:2005 (the official
spec for MP4), which can be obtained from the ISO or found online.

The goals of this project are to run anywhere without compilation (in
particular, many Windows and Mac OS X users have trouble getting
qt-faststart.c compiled), to run about as fast as the C version, to be more
user friendly, and to use less actual lines of code doing so.

This is a .NET port of the python library available here: https://github.com/danielgtaylor/qtfaststart
