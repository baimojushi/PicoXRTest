import os
src = r'E:\Projects\UnitySplatsGallery\Assets\GalleryRuntime\Collision\collision_proxy_ruf.obj'
dst = r'E:\Projects\UnitySplatsGallery\Assets\GalleryRuntime\Collision\collision_proxy_ruf_clean.obj'
lines = open(src, 'r').readlines()
print('Read %d lines' % len(lines))
with open(dst, 'w') as f:
    for line in lines:
        if line.startswith('v '):
            parts = line.split()
            if len(parts) >= 4:
                f.write('v %s %s %s\n' % (parts[1], parts[2], parts[3]))
            else:
                f.write(line)
        else:
            f.write(line)
print('Written. Exists:', os.path.exists(dst))
if os.path.exists(dst):
    print('Size:', os.path.getsize(dst))