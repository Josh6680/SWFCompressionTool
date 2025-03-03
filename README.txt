Command line tool to decompress and recompress Ruffle/Adobe Flash SWF files.

This tool was created to decompress SWF files (with either CWS/ZLIB or ZWS/LZMA headers) to an uncompressed FWS header copy, enabling direct edits to be made to the uncompressed copy, then recompress the file to either CWS/ZLIB or the newer ZWS/LZMA format, to reduce the size and/or update the format.

Usage: SWFCompressionTool.exe "Path/To/File.swf"