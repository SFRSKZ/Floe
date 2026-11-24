"""Compiles every WPF pixel shader of the dock once, so it never runs the HLSL compiler on its UI thread.

Run after editing any src/Floe.App/Effects/**/*.hlsl:  python tools/compile-shaders.py
Writes src/Floe.App/Effects/ShaderCache/<key>.cso, keyed like Floe.Core.Services.ShaderKey.
"""
import ctypes
import glob
import hashlib
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Floe.App", "Effects")
OUT = os.path.join(ROOT, "ShaderCache")
TARGETS = (b"ps_3_0", b"ps_2_0")

d3d = ctypes.WinDLL("d3dcompiler_47.dll")
compile_ = d3d.D3DCompile
compile_.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_void_p,
                     ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                     ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
compile_.restype = ctypes.c_long
blob_ptr = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)
blob_size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)
release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)


def method(obj, index, proto):
    vtable = ctypes.cast(obj, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p)))[0]
    return proto(vtable[index])


def blob_bytes(blob):
    ptr = method(blob, 3, blob_ptr)(blob)
    size = method(blob, 4, blob_size)(blob)
    return ctypes.string_at(ptr, size)


def key(source, target):
    return hashlib.sha256(source.replace(b"\r", b"") + b"|" + target).hexdigest()[:16]


os.makedirs(OUT, exist_ok=True)
for old in glob.glob(os.path.join(OUT, "*.cso")):
    os.remove(old)

written = 0
for path in sorted(glob.glob(os.path.join(ROOT, "**", "*.hlsl"), recursive=True)):
    source = open(path, "rb").read()
    for target in TARGETS:
        code, errors = ctypes.c_void_p(), ctypes.c_void_p()
        # same call as ShaderCompiler.CompileBytes: entry main, optimization level 3
        hr = compile_(source, len(source), b"MetalRim.hlsl", None, None, b"main", target, 1 << 15, 0,
                      ctypes.byref(code), ctypes.byref(errors))
        if errors.value:
            method(errors, 2, release)(errors)
        if hr < 0 or not code.value:
            continue  # this profile can't hold the shader; the dock never asks for it there
        with open(os.path.join(OUT, key(source, target) + ".cso"), "wb") as f:
            f.write(blob_bytes(code))
        method(code, 2, release)(code)
        written += 1
        print(f"{os.path.relpath(path, ROOT):40s} {target.decode()}")

print(f"{written} shaders")
