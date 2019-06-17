using System;
using TMPro;

namespace CircleTag
{
    public static class Generator
    {
        public class Settings
        {
            public double Angle = 0.0;
            public int Width = 512;
            public int Height = 512;
            public uint BackgroundColor = 0x00ffffff;
            public uint ForegroundColor = 0x44ffffff;
            public double StartingRadius = 0.3;
            public double EndingRadius = 0.95;
            public int BytesPerLayer = 2;
        }
        
        private static int _segments;
        private static double _segmentScale;
        private static double _radiusScale;
        private static double _imageLengthNormalizerCached;
        private static int _layerCount;
        private static Settings _settings;
        
        public static unsafe byte[] From(byte[] bytes, Settings settings = null)
        {
            _settings = settings ?? new Settings();
            _segments = _settings.BytesPerLayer * 8 + 1;
            _segmentScale = 1.0 / (360.0 / _segments);
            _radiusScale = 1.0 / (_settings.EndingRadius - _settings.StartingRadius);
            byte[] newBytes = new byte[bytes.Length + 2];
            Array.Copy(bytes, 0, newBytes, 1, bytes.Length);
            byte hash = CodeReader.CalculateHash(bytes);
            newBytes[0] = (byte) bytes.Length;
            newBytes[bytes.Length + 1] = hash;
            bytes = newBytes;
            bytes = PadBytes(bytes);
            uint size = (uint)_settings.Width * (uint)_settings.Height;
            int halfWidth = _settings.Width / 2;
            int halfHeight = _settings.Height / 2;
            _imageLengthNormalizerCached = 1.0 / Math.Min(halfWidth, halfHeight);
            _layerCount = bytes.Length / _settings.BytesPerLayer + 1;
            byte[] output = new byte[size * 4];
            fixed (byte* pixelBytes = output)
            {
                uint* pixels = (uint*)pixelBytes;
                for (int pixelIndex = 0; pixelIndex < size; pixelIndex++)
                {
                    int y = pixelIndex / _settings.Height;
                    int x = pixelIndex % _settings.Height;
                    bool hasPixel = HasPixel(x, y, halfWidth, halfHeight, bytes);
                    pixels[pixelIndex] = hasPixel ? _settings.ForegroundColor : _settings.BackgroundColor;
                }
            }
            return output;
        }

        private static byte[] PadBytes(byte[] bytes)
        {
            int neededPadding = bytes.Length % _settings.BytesPerLayer;
            if (neededPadding <= 0) return bytes;
            byte[] paddedBytes = new byte[bytes.Length + (_settings.BytesPerLayer - neededPadding)];
            Array.Copy(bytes, paddedBytes, bytes.Length);
            return paddedBytes;
        }

        private static bool HasPixel(int x, int y, int centerX, int centerY, byte[] data)
        {
            // Calculate pixel offset from center
            double offX = x - centerX;
            double offY = y - centerY;
            
            // Calculate normalized distance from center
            double length = Math.Sqrt(offX * offX + offY * offY);
            double distance = length * _imageLengthNormalizerCached;
            
            // Map point to inside of StartingRadius and EndingRadius
            double pointDistance = (distance - _settings.StartingRadius) * _radiusScale;
            if (pointDistance < 0.0 || pointDistance > 1.0)
            {
                // Point is outside of starting and ending radius
                return false;
            }
            
            // Calculate the layer
            int layer = (int)(pointDistance * _layerCount);
            
            // Calculate the segment number on layer
            double invLength = 1.0 / length;
            double normX = offX * invLength;
            double normY = offY * invLength;
            int segment = GetSegment(normX, normY);
            
            // Draw inner ring so that there is an empty mark for the segment 0, except when there is no data
            if (layer <= 0) return data.Length < 3 || segment > 0;
            
            // Offset layer to accommodate for the inner ring
            layer--;

            // The first segment should be solid to allow finding the orientation, except when there is no data
            if (segment == 0) return data.Length > 3;

            // Reduce one segment off so the number of segments aligns to number of bytes
            segment--;

            // Calculate byte array index for the layer and segment
            int byteOffset = segment / 8;
            int dataIndex = layer * _settings.BytesPerLayer + byteOffset;
            
            // Calculate mask byte for reading the byte array
            byte mask = (byte)(0b00000001 << segment % 8);
            
            // Return empty pixel for where the data has ended
            if (data.Length <= dataIndex) return false;
            
            // Return if bit is true or false
            return (data[dataIndex] & mask) > 0;
        }

        private static int GetSegment(double normX, double normY)
        {
            const double oneOverPiTimes180 = 1.0 / Math.PI * 180.0;
            double angle = Math.Atan2(normY, -normX) * oneOverPiTimes180 + 180 + _settings.Angle;
            if (angle > 360.0) angle -= 360.0;
            double segment = Math.Floor(angle * _segmentScale);
            return (int)segment;
        }
    }
}