import time, ctypes, sys
from ctypes import wintypes
from PIL import ImageGrab
user32 = ctypes.windll.user32
# find window by class/title "松光启动器"
hwnd = user32.FindWindowW(None, "松光启动器")
if not hwnd:
    print("window not found"); sys.exit(1)
rect = wintypes.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))
m = 24  # margin to reveal rounded-corner transparency against desktop
box = (rect.left - m, rect.top - m, rect.right + m, rect.bottom + m)
img = ImageGrab.grab(box, all_screens=True)
img.save("dev/shots/corners.png")
print("saved", img.size, "rect", rect.left, rect.top, rect.right, rect.bottom)
