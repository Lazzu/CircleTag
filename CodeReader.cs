
using System;

namespace DefaultNamespace
{
    public static class CodeReader
    {
        private const uint ColorDifferenceTolerance = 250;
        private static byte[] _debugBytes;
        
        public static byte[] Read(byte[] image, int width, int height, byte[] debugBytes = null)
        {
            #if DEBUG
            _debugBytes = debugBytes;
            #endif
            CalculateImageCenter(width, height, out int imageCenterX, out int imageCenterY);
            uint initialColor = ReadInitialColor(image, width, height);
            bool foundCenter = false;
            int maxIterations = Math.Min(width, height) / 4;
            int iterations = 0;
            int codeCenterX = -1;
            int codeCenterY = -1;
            
            while (!foundCenter && iterations < maxIterations)
            {
                int offX = iterations * 4;
                int offY = iterations * 4;
                if (iterations % 2 == 0)
                {
                    offX = -offX;
                    offY = -offY;
                }
                if (iterations % 4 == 0)
                {
                    offY = -offY;
                }

                int x = imageCenterX + offX;
                int y = imageCenterY + offY;

                foundCenter = FindTagCenterCoordinates(image, initialColor, x, y, width, out codeCenterX, out codeCenterY);
                iterations++;
            }
            
            if (!foundCenter)
            {
                return null;
            }

            FindRadius(image, width, initialColor, codeCenterX, codeCenterY, out double radius);
            if (radius < 0)
            {
                return null;
            }

            FindStartingAngleAndSegmentSize(image, initialColor, codeCenterX, codeCenterY, width, radius,
                out double angle, out double segmentSize);
            if (segmentSize < 0 || angle < 0)
            {
                return null;
            }

            FindLayerSizeAndCount(image, initialColor, codeCenterX, codeCenterY, width, radius, angle,
                segmentSize, out int layerSize, out int layerCount);
            if (layerSize < 0)
            {
                return null;
            }
            
            return ReadData(image, initialColor, codeCenterX, codeCenterY, width, radius, layerSize, angle,
                segmentSize, layerCount);
        }

        private static void FindRadius(byte[] image, int width, uint initialColor, int codeCenterX, int codeCenterY, out double radius)
        {
            CalculateDistances(image, width, width / 2, initialColor, codeCenterX, codeCenterY, 
                out double a, out double b, out double c, out double d);

            if (a < 0 || b < 0 || c < 0 || d < 0)
            {
                radius = -1;
                return;
            }

            double ab = Math.Abs(a - b);
            double cd = Math.Abs(c - d);

            radius = Math.Min(ab, cd) / 2.0;
        }

        private static void FindLayerSizeAndCount(byte[] image, uint initialColor, int codeCenterX, int codeCenterY, 
            int width, double radius, double angle, double segmentSize, out int layerSize, out int layerCount)
        {
            angle += segmentSize / 2.0;
            int startingIndex = (int)(radius);
            uint dataColor = 0;
            layerSize = 0;
            int usedWidth = Math.Min(codeCenterX, width / 2);
            for (int i = startingIndex; i < width; i++)
            {
                CalculateCoords(angle, i, out int x, out int y);

                x += codeCenterX;
                y += codeCenterY;
                
                if(x < 0 || y < 0 || x > width || y > width) continue;
                
                uint color = ReadColor(image, x, y, width);
                
                int diff = ColorDiff(color, initialColor);
                if(diff < ColorDifferenceTolerance)
                {
                    continue;
                }
                dataColor = color;
                layerSize = i - startingIndex - 1;
                startingIndex = i;
                break;
            }

            if (layerSize == 0 || layerSize == usedWidth)
            {
                layerCount = 0;
                return;
            }
            
            int datasize = 0;
            for (int i = startingIndex; i < width; i++)
            {
                CalculateCoords(angle, i, out int x, out int y);

                x += codeCenterX;
                y += codeCenterY;
                
                if(x < 0 || y < 0 || x > width || y > width) continue;
                
                uint color = ReadColor(image, x, y, width);
                
                int diff = ColorDiff(color, dataColor);
                if(diff < ColorDifferenceTolerance)
                {
                    continue;
                }
                datasize = i - startingIndex - 1;
                break;
            }

            layerCount = (int)Math.Round((double)datasize / (double)layerSize);
        }

        private static int CalculateSegmentCount(double segmentSize)
        {
            int segments = (int) Math.Round(Math.PI * 2 / segmentSize);
            // If the number of segments - 1 can be divided by 8 equally, it should be a correct amount of segments
            return (segments -1) % 8 == 0 ? segments : -1;
        }

        private static byte[] ReadData(byte[] image, uint initialColor, int codeCenterX, int codeCenterY, int width, 
            double radius, int layerSize, double startingAngle, double segmentSize, int layerCount)
        {
            int segments = CalculateSegmentCount(segmentSize);
            if (segments < 0) return null;
            double halfSegment = segmentSize / 2.0;
            int byteIndex = 0;
            int bitIndex = 0;
            byte readByte = 0;
            byte sizeByte = 0;
            byte[] bytes = null;
            for (int layer = 0; layer < layerCount; layer++)
            {
                for (int segment = 1; segment < segments; segment++)
                {
                    double angle = startingAngle + segment * segmentSize + halfSegment;
                    double distance = radius + layerSize + layer * layerSize + layerSize / 2.0; 
                    CalculateCoords(angle, distance, out int x, out int y);
                    x += codeCenterX;
                    y += codeCenterY;
                    if (CheckPixel(image, x, y, width, initialColor))
                    {
                        readByte |= (byte)(0b00000001 << bitIndex);
                    }
                    bitIndex++;
                    if (bitIndex < 8) continue;
                    bitIndex = 0;
                    if (sizeByte == 0)
                    {
                        sizeByte = readByte;
                        readByte = 0;
                        bytes = new byte[sizeByte];
                        continue;
                    }
                    if (byteIndex == sizeByte)
                    {
                        byte hash = CalculateHash(bytes);
                        bool valid = hash == readByte;
                        return valid ? bytes : null;
                    }
                    bytes[byteIndex] = readByte;
                    readByte = 0;
                    byteIndex++;
                }
            }
            return null;
        }

        public static byte CalculateHash(byte[] bytes)
        {
            byte hash = 0;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash += bytes[i];
                }
            }
            return hash;
        }

        private static void CalculateCoords(double angle, double distance, out int x, out int y)
        {
            x = (int) Math.Round(Math.Cos(-angle) * distance);
            y = (int) Math.Round(Math.Sin(-angle) * distance);
        }

        private static void FindStartingAngleAndSegmentSize(byte[] image, uint initialColor, int codeCenterX, 
            int codeCenterY, int width, double radius, out double angle, out double segmentSize)
        {
            angle = -1.0;
            segmentSize = -1.0;
            double step = Math.PI / 360.0;
            double current = 0.0;
            for (int i = 0; current < Math.PI * 2; i++)
            {
                current = step * i;
                CalculateCoords(current, radius + 5, out int x, out int y);

                x += codeCenterX;
                y += codeCenterY;
                
                if(x < 0 || y < 0 || x > width || y > width) continue;
                
                // Find the first pixel in the notch of the first layer
                if (angle < 0 && !CheckPixel(image, x, y, width, initialColor))
                {
                    angle = current - step;
                }
                
                // Find the last pixel in the notch of the first layer
                if (angle > 0 && CheckPixel(image, x, y, width, initialColor))
                {
                    segmentSize = current - angle;
                    return;
                }
            }

            angle = -1;
            segmentSize = -1;
        }

        private static void CalculateImageCenter(int width, int height, out int centerX, out int centerY)
        {
            centerX = width / 2;
            centerY = height / 2;
        }

        private static bool FindTagCenterCoordinates(byte[] image, uint initialColor, int imageCenterX, int imageCenterY, int width, out int foundCenterX, out int foundCenterY)
        {
            foundCenterX = -1;
            foundCenterY = -1;

            int size = Math.Min(imageCenterX, imageCenterY);
            
            CalculateDistances(image, width, size, initialColor, imageCenterX, imageCenterY, 
                out double a, out double b, out double c, out double d);

            if (a < 0 || b < 0 || c < 0 || d < 0)
            {
                return false;
            }

            var ab = b - a;
            var cd = d - c;

            foundCenterX = (int) Math.Floor(a + (ab / 2));
            foundCenterY = (int) Math.Floor(c + (cd / 2));
            
            return true;
        }

        private static void CalculateDistances(byte[] image, int width, int size, uint initialColor, int centerX, 
            int centerY, out double a, out double b, out double c, out double d)
        {
            a = -1;
            b = -1;
            c = -1;
            d = -1;
            int x;
            int y;

            for (int i = 1; i < size; i++)
            {
                x = centerX - i;
                y = centerY;
                if(x >= 0 && a < 0 && CheckPixel(image, x, y, width, initialColor))
                {
                    a = x + 1;
                }
                
                x = centerX + i;
                y = centerY;
                if(x < width && b < 0 && CheckPixel(image, x, y, width, initialColor))
                {
                    b = x - 1;
                }
                
                x = centerX;
                y = centerY - i;
                if(y >= 0 && c < 0 && CheckPixel(image, x, y, width, initialColor))
                {
                    c = y + 1;
                }
                
                x = centerX;
                y = centerY + i;
                if(y < width && d < 0 && CheckPixel(image, x, y, width, initialColor))
                {
                    d = y - 1;
                }
                
                if(a >= 0 && b >= 0 && c >= 0 && d >= 0) return;
            }
        }

        private static bool CheckPixel(byte[] image, int x, int y, int width, uint initialColor)
        {
            uint color = ReadColor(image, x, y, width);
            return color != initialColor && ColorDiff(initialColor, color) > ColorDifferenceTolerance;
        }

        private static int ColorDiff(uint color1, uint color2)
        {
            int diff = 0;
            for (int i = 0; i < 4; i++)
            {
                int bitOffset = (i * 8);
                uint mask = ((uint)0x000000ff) << bitOffset;
                int value1 = (int)((color1 & mask) >> bitOffset);
                int value2 = (int)((color2 & mask) >> bitOffset);
                diff += Math.Abs(value1 - value2);
            }
            return diff;
        }

        private static uint ReadInitialColor(byte[] image, int width, int height)
        {
            CalculateImageCenter(width, height, out int imageCenterX, out int imageCenterY);
            return ReadColor(image, imageCenterX, imageCenterY, width);
        }
        
        private static uint ReadColor(byte[] image, int x, int y, int width)
        {
            int index = (y * width + x) * 4;
            uint color = 0;
            if (index >= image.Length - 4) return color;
            for (int i = 0; i < 4; i++)
            {
                int bitOffset = i * 8;
                uint value = image[index + i];
                value = value << bitOffset;
                color |= value;
            }
            return color;
        }
        
        private static void WriteColor(int x, int y, int width, uint color)
        {
            #if !DEBUG
            return;
            #endif
            if(_debugBytes == null || x < 0 || y < 0 || width < 0) return;
            int index = (y * width + x) * 4;
            if (index >= _debugBytes.Length - 4) return;
            for (int i = 0; i < 4; i++)
            {
                int bitOffset = i * 8;
                int mask = 0x000000ff << bitOffset;
                byte value = (byte) ((color & mask) >> bitOffset);
                _debugBytes[index + i] = value;
            }
        }
    }
}
