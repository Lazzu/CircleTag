using System;

namespace CircleTag
{
    internal class TagImage
    {
        public byte[] Bytes;
        public uint ColorDifferenceTolerance;
        public int CenterX;
        public int CenterY;
        public int CodeCenterX;
        public int CodeCenterY;
        public uint BaseColor;
        public double CodeRadius;
        public double CodeStartingAngle;
        public double CodeSegmentSize;
        public int CodeLayerSize;
        public int CodeLayerCount;
        private int _width = -1;
        private int _height = -1;
        public int Width
        {
            get => _width;
            set
            {
                _width = value;
                CenterX = _width / 2;
            }
        }
        public int Height
        {
            get => _height;
            set
            {
                _height = value;
                CenterY = _height / 2;
            }
        }
        
        public uint ReadColor(int x, int y)
        {
#if DEBUG
            if(x < 0) throw new ArgumentOutOfRangeException(nameof(x), $"Value ({x}) can not be less than zero.");
            if(y < 0) throw new ArgumentOutOfRangeException(nameof(y), $"Value ({y}) can not be less than zero.");
            if(x > _width) throw new ArgumentOutOfRangeException(nameof(x), $"Value ({x}) can not be more than width ({_width}).");
            if(y > _height) throw new ArgumentOutOfRangeException(nameof(y), $"Value ({y}) can not be more than height ({_height}).");
#endif
            int index = (y * _width + x) * 4;
            uint color = 0;
            if (index >= Bytes.Length - 4) return color;
            for (int i = 0; i < 4; i++)
            {
                color |= (uint)Bytes[index + i] << (i * 8);
            }
            return color;
        }
        
        public bool CheckPixel(int x, int y)
        {
            uint color = ReadColor(x, y);
            return color != BaseColor && ColorDiff(color) > ColorDifferenceTolerance;
        }
        
        public bool CheckPixel(int x, int y, out uint diff)
        {
            uint color = ReadColor(x, y);
            diff = ColorDiff(color);
            return diff > ColorDifferenceTolerance;
        }

        public uint ColorDiff(uint color)
        {
            return ColorDiff(BaseColor, color);
        }
        
        public static uint ColorDiff(uint color1, uint color2)
        {
            uint diff = 0;
            for (int i = 0; i < 4; i++)
            {
                int bitOffset = i * 8;
                int mask = 0x000000ff << bitOffset;
                int value1 = (int)((color1 & mask) >> bitOffset);
                int value2 = (int)((color2 & mask) >> bitOffset);
                int colorChannelDiff = value1 - value2;
                //diff += Math.Abs(colorChannelDiff);
                // Minor optimization. This works because we expect the colorChannelDiff to not be anywhere near
                // the int.MinValue, which if it was would screw up this calculation.
                diff += (uint)((colorChannelDiff + (colorChannelDiff >> 31)) ^ (colorChannelDiff >> 31));
            }
            return diff;
        }
        
        public void WriteColor(int x, int y, uint color)
        {
            if(Bytes == null || x < 0 || y < 0 || _width < 0) return;
            int index = (y * _width + x) * 4;
            if (index >= Bytes.Length - 4) return;
            for (int i = 0; i < 4; i++)
            {
                int bitOffset = i * 8;
                int mask = 0x000000ff << bitOffset;
                byte value = (byte) ((color & mask) >> bitOffset);
                Bytes[index + i] = value;
            }
        }
    }
}