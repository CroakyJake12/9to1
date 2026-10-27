"""Parse UCM files with ALSA's real syntax parser; hardware routing remains untested."""
import ctypes
from pathlib import Path
lib=ctypes.CDLL('libasound.so.2')
ptr=ctypes.c_void_p
lib.snd_input_stdio_open.argtypes=[ctypes.POINTER(ptr),ctypes.c_char_p,ctypes.c_char_p]
lib.snd_config_top.argtypes=[ctypes.POINTER(ptr)]
lib.snd_config_load.argtypes=[ptr,ptr]
lib.snd_config_delete.argtypes=[ptr]
lib.snd_input_close.argtypes=[ptr]
for path in Path(__file__).with_name('runtime').joinpath('audio').glob('*.conf'):
    stream=ptr();config=ptr()
    assert lib.snd_input_stdio_open(ctypes.byref(stream),str(path).encode(),b'r')>=0,path
    assert lib.snd_config_top(ctypes.byref(config))>=0,path
    try:
        assert lib.snd_config_load(config,stream)>=0,path
    finally:
        lib.snd_config_delete(config);lib.snd_input_close(stream)
    print('ALSA syntax parsed:',path.name)
