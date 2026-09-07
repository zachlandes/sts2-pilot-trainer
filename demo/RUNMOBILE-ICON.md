# Runmobile icon

*2026-09-07T19:32:28Z by Showboat 0.6.1*
<!-- showboat-id: c5e84a63-7a20-4bed-ab06-fc6f4296f1df -->

Runmobile’s mod-list icon is the captain-approved wagon, rendered at the game’s native 64×64 resource size.

```bash {image}
![Runmobile’s wagon icon at its actual 64×64 size](runmobile-icon-64.png)
```

![Runmobile’s wagon icon at its actual 64×64 size](73a49031-2026-09-07.png)

```python3
from pathlib import Path
import struct

image = Path("runmobile-icon-64.png").read_bytes()
assert image[:8] == b"\x89PNG\r\n\x1a\n"
assert image[12:16] == b"IHDR"
width, height, bit_depth, color_type = struct.unpack(">IIBB", image[16:26])
assert (width, height, bit_depth, color_type) == (64, 64, 8, 6)
print(f"Runmobile mod-list icon: {width}×{height}, 8-bit RGBA")
```

```output
Runmobile mod-list icon: 64×64, 8-bit RGBA
```
