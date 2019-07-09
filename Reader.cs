
using System;
using System.Net.Security;
using UnityEngine;
using Random = System.Random;

namespace CircleTag
{
    public static class Reader
    {
        private static TagImage _debugImage;
        
        public static byte[] Read(byte[] bytes, int width, int height, uint tolerance, byte[] debugBytes = null)
        {
            TagImage tagImage = new TagImage()
            {
                Bytes = bytes,
                Width = width,
                Height = height,
                ColorDifferenceTolerance = tolerance
            };
            
#if DEBUG
            if (debugBytes != null)
            {
                _debugImage = new TagImage()
                {
                    Bytes = debugBytes,
                    Width = width,
                    Height = height
                };
            }
#endif

            tagImage.BaseColor = tagImage.ReadColor(tagImage.CenterX, tagImage.CenterY);
            int maxIterations = Mathf.Min(width, height) / 32;

            if (maxIterations <= 0)
            {
                return null;
            }
            
            if (!TryFindTagCenterCoordinatesAndRadius(tagImage, maxIterations))
            {
                return null;
            }

            if (!TryFindStartingAngleAndSegmentSize(tagImage))
            {
                return null;
            }

            if (!TryFindLayerSizeAndCount(tagImage))
            {
                return null;
            }
            
            return TryReadData(tagImage, out byte[] readBytes) ? readBytes : null;
        }

        private static bool TryFindTagCenterCoordinatesAndRadius(TagImage tagImage, int maxIterations)
        {
            // Try finding the tag center iteratively
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Search center in four pixel steps
                int offX = iteration * 5;
                int offY = iteration * 5;
                
                // Check if we went past the half of the size of the image
                if(offX > tagImage.CenterX) return false;
                if(offY > tagImage.CenterY) return false;
                
                // Alternate offset direction between iterations
                if (iteration % 2 == 0)
                {
                    offX = -offX;
                    offY = -offY;
                }
                if (iteration % 4 == 0)
                {
                    offY = -offY;
                }
                if (iteration % 3 == 0)
                {
                    offX = -offX;
                }

                // Try finding the center with the given offset
                if (TryFindTagCenterCoordinates(tagImage, offX, offY, out int codeCenterX, out int codeCenterY, out int radius))
                {
                    tagImage.CodeCenterX = codeCenterX;
                    tagImage.CodeCenterY = codeCenterY;
                    tagImage.CodeRadius = radius;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindLayerSizeAndCount(TagImage tagImage)
        {
            int layerSize = 0;
            int layerCount = 0;
            double angle = tagImage.CodeStartingAngle + tagImage.CodeSegmentSize / 2.0;
            int startingIndex = tagImage.CodeRadius;
            uint dataColor = 0;
            int usedWidth = Math.Min(tagImage.CodeCenterX, tagImage.Width / 2);
            
            // Find layer size
            for (int i = startingIndex; i < tagImage.Width; i++)
            {
                CalculateCoords(tagImage, angle, i, out int x, out int y);
                
                if(x < 0 || y < 0 || x > tagImage.Width || y > tagImage.Height) continue;
                
                uint color = tagImage.ReadColor(x, y);
                
                uint diff = tagImage.ColorDiff(color);
                if(diff < tagImage.ColorDifferenceTolerance)
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
                return false;
            }
            
            // Find size of the tag data block (layer size * layer count, starting from the first data layer)
            int dataSize = -1;
            for (int i = startingIndex; i < tagImage.Width; i++)
            {
                CalculateCoords(tagImage, angle, i, out int x, out int y);
                
                if(x < 0 || y < 0 || x > tagImage.Width || y > tagImage.Height) continue;
                
                uint color = tagImage.ReadColor(x, y);
                
                uint diff = TagImage.ColorDiff(color, dataColor);
                if(diff < tagImage.ColorDifferenceTolerance)
                {
                    continue;
                }
                dataSize = i - startingIndex - 1;
                break;
            }
            if (dataSize < 0)
            {
                return false;
            }

            layerCount = (int)Math.Round((double)dataSize / (double)layerSize);
            tagImage.CodeLayerSize = layerSize;
            tagImage.CodeLayerCount = layerCount;
            return true;
        }

        private static int CalculateSegmentCount(double segmentSize)
        {
            int segments = (int) Math.Round(Math.PI * 2 / segmentSize);
            // If the number of segments - 1 can be divided by 8 equally, it should be a correct amount of segments
            return (segments -1) % 8 == 0 ? segments : -1;
        }

        private static bool TryReadData(TagImage tagImage, out byte[] bytes)
        {
            bytes = null;
            int segments = CalculateSegmentCount(tagImage.CodeSegmentSize);
            if (segments <= 0) return false;
            double halfSegment = tagImage.CodeSegmentSize / 2.0;
            int byteIndex = 0;
            byte currentBit = 1;
            byte currentByte = 0;
            byte sizeByte = 0;
            int halfCodeLayerSize = tagImage.CodeLayerSize / 2;
            double startingAngle = tagImage.CodeStartingAngle + halfSegment;
            for (int layer = 0; layer < tagImage.CodeLayerCount; layer++)
            {
                int layerSize = tagImage.CodeSegmentSize * layer;
                for (int segment = 1; segment < segments; segment++)
                {
                    // Calculate coordinates
                    double angle = startingAngle + segment * tagImage.CodeSegmentSize;
                    int distance = tagImage.CodeRadius + tagImage.CodeLayerSize + layerSize + halfCodeLayerSize; 
                    CalculateCoords(tagImage, angle, distance, out int x, out int y);
                    
                    // Read bit
                    if (tagImage.CheckPixel(x, y))
                    {
                        currentByte |= currentBit;
                    }
                    
                    // Advance bit and check if there are more bits
                    currentBit <<= 1;
                    if (currentBit > 0) continue;
                    
                    // No more bits, handle byte
                    currentBit = 1;
                    if (sizeByte == 0)
                    {
                        // Size not read yet, use the current byte value as data size and continue reading
                        sizeByte = currentByte;
                        currentByte = 0;
                        bytes = new byte[sizeByte];
                        continue;
                    }
                    if (byteIndex == sizeByte)
                    {
                        // We have read all bytes, check hash byte and return
                        byte hash = CalculateHash(bytes);
                        return hash == currentByte;
                    }
                    
                    // Push the current byte to the byte array and continue reading
                    bytes[byteIndex] = currentByte;
                    currentByte = 0;
                    byteIndex++;
                }
            }
            return false;
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

        private static void CalculateCoords(TagImage image, double angle, int distance, out int x, out int y)
        {
            x = (int) Math.Round(Math.Cos(-angle) * distance) + image.CodeCenterX;
            y = (int) Math.Round(Math.Sin(-angle) * distance) + image.CodeCenterY;
        }

        private static bool TryFindStartingAngleAndSegmentSize(TagImage tagImage)
        {
            double startingAngle = -1;
            const double angleStepSize = Math.PI / 360.0 / 2.0;
            const int radiusOffset = 2;
            double currentAngle = 0.0;
            for (int step = 0; currentAngle < Math.PI * 2; step++)
            {
                currentAngle = angleStepSize * step;
                
                CalculateCoords(tagImage, currentAngle, tagImage.CodeRadius + radiusOffset, out int x, out int y);
                
                _debugImage?.WriteColor(x, y, 0xff0000ff);
                
                if(x < 0 || y < 0 || x > tagImage.Width || y > tagImage.Width)
                {
                    continue;
                }

                bool hasPixel = tagImage.CheckPixel(x, y);
                
                // Find the first pixel in the notch of the first layer
                if (startingAngle < 0 && !hasPixel)
                {
                    startingAngle = currentAngle - angleStepSize;
                    _debugImage?.WriteColor(x, y, 0xffff00ff);
                }
                
                // Find the last pixel in the notch of the first layer
                if (startingAngle > 0 && hasPixel)
                {
                    tagImage.CodeStartingAngle = startingAngle;
                    tagImage.CodeSegmentSize = currentAngle - startingAngle;
                    _debugImage?.WriteColor(x, y, 0xff00ffff);
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindTagCenterCoordinates(TagImage tagImage, int offX, int offY, out int foundCenterX, out int foundCenterY, out int radius)
        {
            foundCenterX = -1;
            foundCenterY = -1;
            radius = -1;
            
            if (!TryCalculateDistances(tagImage, tagImage.CenterX + offX, tagImage.CenterY + offY, out int left, out int right, out int down, out int up))
            {
                return false;
            }

            int width = right - left;
            int height = up - down;

            foundCenterX = (left + width) >> 1;
            foundCenterY = (down + height) >> 1;

            int diff = width - height;
            // Optimized diff = abs(diff), works as long as diff is not int.MinValue, which it is assumed to not be in this case.
            diff = (diff + (diff >> 31)) ^ (diff >> 31);

            // If the difference is too large, failed to read the image
            if (diff > 1) return false;
            
            // Then calculate the radius from given width and height
            radius = (width < height ? width : height) >> 1;
            
            return true;
        }

        private static bool TryCalculateDistances(TagImage tagImage, int originX, int originY, out int left, out int right, out int down, out int up)
        {
            left = -1;
            right = -1;
            down = -1;
            up = -1;
            int size = tagImage.Width / 2;
            uint diff = 0;

            for (int i = 1; i < size; i++)
            {
                int x = originX - i;
                int y = originY;
                if(x >= 0 && x < tagImage.Width)
                {
                    if (left < 0 && tagImage.CheckPixel(x, y, out diff))
                    {
                        left = x + 1;
                    }
                    _debugImage?.WriteColor(x, y, diff | 0xff000000);
                }
                
                x = originX + i;
                if(x >= 0 && x < tagImage.Width)
                {
                    if (right < 0 && tagImage.CheckPixel(x, y, out diff))
                    {
                        right = x - 1;
                    }
                    _debugImage?.WriteColor(x, y, diff | 0xff000000);
                }
                
                x = originX;
                y = originY - i;
                if(y >= 0 && y < tagImage.Height)
                {
                    if (down < 0 && tagImage.CheckPixel(x, y, out diff))
                    {
                        down = y + 1;
                    }
                    _debugImage?.WriteColor(x, y, diff | 0xff000000);
                }
                
                y = originY + i;
                if(y >= 0 && y < tagImage.Height)
                {
                    if (up < 0 && tagImage.CheckPixel(x, y, out diff))
                    {
                        up = y - 1;
                    }
                    _debugImage?.WriteColor(x, y, diff | 0xff000000);
                }
                
                if(left >= 0 && right >= 0 && down >= 0 && up >= 0) return true;
            }

            return false;
        }
    }
}
