"""Compress-StaticAsset.py — named precompression helper (LOCAL-PYTHON-20261103).

Usage: <local-python> Compress-StaticAsset.py <input> <output> <br|gz> [quality]
  br: brotli quality 11 default (override 0-11); gz: gzip level 9.
Writes atomically (temp file + os.replace), verifies output is nonempty AND
roundtrip-decompresses byte-equal to the input. Exit codes: 0 ok, 2 usage,
3 compress failure, 4 verification failure. Never prints a success it did
not earn; never touches global interpreters (invoked only via resolver).
"""
import gzip
import os
import sys
import tempfile

USAGE = 2
FAIL_COMPRESS = 3
FAIL_VERIFY = 4


def fail(code, msg):
    sys.stderr.write("Compress-StaticAsset FAILED: " + msg + "\n")
    return code


def main(argv):
    if len(argv) not in (4, 5):
        sys.stderr.write("usage: Compress-StaticAsset.py <input> <output> <br|gz> [quality]\n")
        return USAGE
    src, dst, method = argv[1], argv[2], argv[3].lower()
    if method not in ("br", "gz"):
        return fail(USAGE, "method must be br or gz, got %r" % argv[3])
    try:
        quality = int(argv[4]) if len(argv) == 5 else None
    except ValueError:
        return fail(USAGE, "quality must be an integer")
    if not os.path.isfile(src):
        return fail(FAIL_COMPRESS, "input missing: " + src)
    try:
        with open(src, "rb") as fh:
            data = fh.read()
    except OSError as ex:
        return fail(FAIL_COMPRESS, "cannot read %s: %s" % (src, ex))
    try:
        if method == "br":
            import brotli
            q = 11 if quality is None else quality
            if not 0 <= q <= 11:
                return fail(USAGE, "brotli quality must be 0-11")
            blob = brotli.compress(data, quality=q)
        else:
            level = 9 if quality is None else quality
            blob = gzip.compress(data, compresslevel=level)
    except Exception as ex:
        return fail(FAIL_COMPRESS, "%s compress raised %s: %s" % (method, type(ex).__name__, ex))
    if not blob:
        return fail(FAIL_VERIFY, "compressor returned empty output for " + src)
    try:
        if method == "br":
            import brotli
            back = brotli.decompress(blob)
        else:
            back = gzip.decompress(blob)
    except Exception as ex:
        return fail(FAIL_VERIFY, "roundtrip decompress raised %s: %s" % (type(ex).__name__, ex))
    if back != data:
        return fail(FAIL_VERIFY, "roundtrip mismatch for " + src)
    tmpdir = os.path.dirname(os.path.abspath(dst)) or "."
    fd, tmp = tempfile.mkstemp(prefix=".compress-", dir=tmpdir)
    try:
        with os.fdopen(fd, "wb") as fh:
            fh.write(blob)
        os.replace(tmp, dst)
    except OSError as ex:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        return fail(FAIL_COMPRESS, "atomic write failed for %s: %s" % (dst, ex))
    sys.stdout.write("ok %s %d -> %s %d bytes\n" % (src, len(data), dst, len(blob)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
