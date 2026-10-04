import ctypes,json,pathlib,hashlib,os
p=pathlib.Path(os.environ['ASTRA_FRESH_CANVAS_LIBRARY']);b=ctypes.CDLL(str(p))
versions={}
for n,e in [('cake_canvas_abi_version',3),('cake_canvas_selection_api_version',1),('cake_canvas_stroke_mutation_api_version',1),('cake_canvas_quick_erase_api_version',1),('cake_canvas_split_erase_api_version',1),('cake_canvas_user_layer_rank_api_version',1),('cake_canvas_visible_keys_render_api_version',1),('cake_canvas_selector_api_version',1)]:
 f=getattr(b,n);f.restype=ctypes.c_uint32;versions[n]=f();assert versions[n]==e
[getattr(b,n) for n in ['cake_canvas_read_user_layer_ranks','cake_canvas_assign_user_layer_ranks','cake_canvas_render_visible_keys','cake_canvas_preview_selection']]
maps=pathlib.Path('/proc/self/maps').read_text();assert str(p) in maps
print(json.dumps({'pid':os.getpid(),'library':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'versions':versions,'actualMaps':maps}))
