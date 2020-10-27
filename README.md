# Quidditch Archive Tool

This application can be used for running various operations on the contents of a .ccd file, which is used by Harry Potter: Quidditch World Cup. This tool currently supports extracting archives, compressing folders and recursive text search among game archives.

RefPack implementation from OpenSAGE project is used for dealing with the compressed streams: https://github.com/OpenSAGE/OpenSAGE

The compression algorithm is based on the original file by KUDr (original SVN is no longer available).

## Archive Format

Original breakdown from: http://www.watto.org/specs.html?specs=Archive_CCD_FKNL
```
// ARCHIVE HEADER (56 bytes)
  4 - Header (Always string "FKNL")
  4 - Version (Always 2 for QWC files)
  4 - First File Offset
  4 - Deflated Folder Size 
  4 - File Data Length
  4 - Number Of Files
  4 - Unknown (1)
  4 - Header 2 (Some files have the string " FIL" here, and others don't)
  4 - Directory Length
  4 - Unknown (40)
  4 - Directory Offset (56)
  4 - Directory Length
  4 - Number Of Files
  4 - null

// DIRECTORY
  // for each file (16 bytes per entry)
    4 - Filename Offset
    4 - File Offset (relative to the start of the File Data)
    4 - File Length
    4 - File Length

// FILENAME DIRECTORY
  1 - null

  // for each file
    X - Filename
    1 - null Filename Terminator

  X - Padding, filled with junk

// FILE DATA
  // for each file
    X - File Data
```
File section deserves a special mention as the contents of this region are compressed with RefPack. This streams needs to be deflated  before files can be extracted according to the header data above. Unlike most RefPack streams from other EA game archives, first-byte of the CCD streams (normally reserved for flags) are always set to 0x15. This is followed by the standard magic number 0xFB. The proceeding archive length is always 4-bytes long, regardless of the first byte of the archive stream. 
It's also noteworthy that compression uses a reference window size of 0x4000, unlike other games that generally seem to use 0x8000. Using the default value of 0x8000 will cause QWC to crash upon parsing the archive.

## Usage
Compress folder Gryff and its content to Gryff.ccd:
QWCArchiveTool.exe -c "Gryff" -o  ".\Gryff.ccd "

Extract Gryff.ccd to and place contents in ..\OutputDir\Gryff\
QWCArchiveTool.exe -e "C:\Users\Chaos\Desktop\Gryff.ccd" -o  ..\OutputDir

Search for string "snitch" in all cli files within game archives (game folder location is detected via Registry by default):
QWCArchiveTool.exe -s "snitch"
