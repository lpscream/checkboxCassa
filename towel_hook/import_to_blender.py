# Blender: Scripting -> Open -> Run Script. Импортирует towel_hook.obj (мм) в сцену.
import bpy, os
d = os.path.dirname(bpy.data.filepath) or os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.path.dirname(bpy.data.filepath)
path = os.path.join(d, "towel_hook.obj")
sc = bpy.context.scene
sc.unit_settings.system = 'METRIC'; sc.unit_settings.scale_length = 0.001; sc.unit_settings.length_unit = 'MILLIMETERS'
try:
    bpy.ops.wm.obj_import(filepath=path)          # Blender 3.2+
except AttributeError:
    bpy.ops.import_scene.obj(filepath=path)
ob = bpy.context.selected_objects[0]; ob.name = "TowelHook"
bpy.ops.object.shade_smooth()
