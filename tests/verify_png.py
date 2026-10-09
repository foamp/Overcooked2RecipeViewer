"""Independent PNG/CRC/zlib/Pillow validation of production encoder output."""
import struct
import sys
import zlib
from pathlib import Path
from PIL import Image

root = Path(sys.argv[1])
count = 0
for path in [*root.glob('case-*.png'), root / 'long.png', root / 'noise.png']:
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    at, compressed, chunks = 8, bytearray(), []
    while at < len(data):
        size = struct.unpack_from('>I', data, at)[0]
        tag, body = data[at+4:at+8], data[at+8:at+8+size]
        crc = struct.unpack_from('>I', data, at+8+size)[0]
        assert zlib.crc32(tag + body) == crc, (path, tag)
        chunks.append(tag)
        if tag == b'IHDR':
            w, h, depth, mode, compression, filtering, interlace = struct.unpack('>IIBBBBB', body)
            assert (depth, mode, compression, filtering, interlace) == (8, 6, 0, 0, 0)
        if tag == b'IDAT':
            assert size <= 65536
            compressed.extend(body)
        at += 12 + size
    assert chunks[0] == b'IHDR' and chunks[-1] == b'IEND'
    assert b'sRGB' in chunks and at == len(data)
    scanlines = zlib.decompress(compressed)  # Also validates Adler-32.
    assert len(scanlines) == (w * 4 + 1) * h
    pixels = b''.join(scanlines[y*(w*4+1)+1:(y+1)*(w*4+1)] for y in range(h))
    with Image.open(path) as image:
        image.load()
        assert image.mode == 'RGBA' and image.size == (w, h)
        assert image.tobytes() == pixels
        if path.name.startswith('case-'):
            bottom_up = path.with_suffix('.rgba').read_bytes()
            expected = b''.join(bottom_up[y*w*4:(y+1)*w*4] for y in reversed(range(h)))
            assert pixels == expected, 'Preview/PNG pixel mismatch'
        elif path.name == 'long.png':
            assert h == 20000
            assert image.getpixel((0, 0)) == (0, 255, 0, 255)
            assert image.getpixel((16, 19999)) == (255, 0, 0, 255)
        else:
            assert chunks.count(b'IDAT') > 1, 'Expected multiple bounded IDAT chunks'
    count += 1
print(f'PASS: {count} PNGs decoded; CRC, zlib, RGBA, pixel parity and long-image tail verified.')
