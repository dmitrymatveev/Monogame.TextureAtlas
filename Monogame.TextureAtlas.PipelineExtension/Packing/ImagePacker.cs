#region MIT License

/*
 * Copyright (c) 2009-2010 Nick Gravelyn (nick@gravelyn.com), Markus Ewald (cygon@nuclex.org)
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a 
 * copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, including without limitation 
 * the rights to use, copy, modify, merge, publish, distribute, sublicense, 
 * and/or sell copies of the Software, and to permit persons to whom the Software 
 * is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all 
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A 
 * PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION 
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
 * 
 */

#endregion

using Monogame.TextureAtlas;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace sspack
{
    public static class ExtHelp
    {
        public static Size Size(this Bitmap bmp) => new System.Drawing.Size(bmp.Width, bmp.Height);
    }

    public class ImagePacker
    {
        // various properties of the resulting image
        private bool requirePow2, requireSquare;
        private int padding;
        private int outputWidth, outputHeight;

        // the input list of image files
        private List<string> files;

        // some dictionaries to hold the image sizes and destination rectangles
        private readonly Dictionary<string, Bitmap> bitmaps = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Rectangle> imagePlacement = new Dictionary<string, Rectangle>();

        /// <summary>
        /// Packs a collection of images into a single image.
        /// </summary>
        /// <param name="imageFiles">The list of file paths of the images to be combined.</param>
        /// <param name="requirePowerOfTwo">Whether or not the output image must have a power of two size.</param>
        /// <param name="requireSquareImage">Whether or not the output image must be a square.</param>
        /// <param name="maximumWidth">The maximum width of the output image.</param>
        /// <param name="maximumHeight">The maximum height of the output image.</param>
        /// <param name="imagePadding">The amount of blank space to insert in between individual images.</param>
        /// <param name="generateMap">Whether or not to generate the map dictionary.</param>
        /// <param name="outputImage">The resulting output image.</param>
        /// <param name="outputMap">The resulting output map of placement rectangles for the images.</param>
        /// <returns>0 if the packing was successful, error code otherwise.</returns>
        public void PackImage(
            IEnumerable<string> imageFiles,
            bool requirePowerOfTwo,
            bool requireSquareImage,
            int maximumWidth,
            int maximumHeight,
            int imagePadding,
            bool generateMap,
            out Bitmap outputImage,
            out Dictionary<string, Sprite> outputMap)
        {
            files = new List<string>(imageFiles);
            requirePow2 = requirePowerOfTwo;
            requireSquare = requireSquareImage;
            outputWidth = maximumWidth;
            outputHeight = maximumHeight;
            padding = imagePadding;

            outputImage = null;
            outputMap = null;

            // make sure our dictionaries are cleared before starting
            bitmaps.Clear();
            imagePlacement.Clear();
            var imageSpriteRectangles = new Dictionary<string, Sprite>();
            // get the sizes of all the images
            foreach (var image in files.ToArray())
            {
                Bitmap bmp = Bitmap.FromFile(image) as Bitmap;
                if (bmp == null)
                    throw new AtlasException(AtlasException.FailCode.FailedToLoadImage);

                Rectangle rect = GetRealRectangle(bmp);
                if (rect.Equals(Rectangle.Empty))
                {
                    files.Remove(image);
                    continue;
                }

                if (!bmp.Size.Equals(rect.Size))
                {
                    Bitmap adjustedSprite = new Bitmap(rect.Width, rect.Height);
                    Graphics gr = Graphics.FromImage(adjustedSprite);
                    gr.DrawImage(bmp, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
                    gr.Dispose();

                    imageSpriteRectangles.Add(image, new Sprite
                    {
                        Offset = new Microsoft.Xna.Framework.Point(rect.X, rect.Y),
                        Size = new Microsoft.Xna.Framework.Point(bmp.Width, bmp.Height)
                    });

                    bmp.Dispose();
                    bmp = adjustedSprite;
                }
                else
                {
                    imageSpriteRectangles.Add(image, new Sprite
                    {
                        Offset = new Microsoft.Xna.Framework.Point(0, 0),
                        Size = new Microsoft.Xna.Framework.Point(bmp.Width, bmp.Height)
                    });
                }
                bitmaps.Add(image, bmp);
            }

            // sort our files by file size so we place large sprites first
            files.Sort(
                (f1, f2) =>
                {
                    Size b1 = bitmaps[f1].Size();
                    Size b2 = bitmaps[f2].Size();

                    int c = -b1.Width.CompareTo(b2.Width);
                    if (c != 0)
                        return c;

                    c = -b1.Height.CompareTo(b2.Height);
                    if (c != 0)
                        return c;

                    return f1.CompareTo(f2);
                });

            // try to pack the images
            if (!PackImageRectangles())
                throw new AtlasException(AtlasException.FailCode.FailedToPackImage);

            // make our output image
            outputImage = CreateOutputImage();
            if (outputImage == null)
                throw new AtlasException(AtlasException.FailCode.FailedToSaveImage);

            if (generateMap)
            {
                // go through our image placements and replace the width/height found in there with
                // each image's actual width/height (since the ones in imagePlacement will have padding)
                string[] keys = new string[imagePlacement.Keys.Count];
                imagePlacement.Keys.CopyTo(keys, 0);
                foreach (var k in keys)
                {
                    // get the actual size
                    Size s = bitmaps[k].Size();

                    // get the placement rectangle
                    Rectangle r = imagePlacement[k];

                    // set the proper size
                    r.Width = s.Width;
                    r.Height = s.Height;

                  //  var src = imageSpriteRectangles[k];
                    // insert back into the dictionary
                    imageSpriteRectangles[k].Source = new Microsoft.Xna.Framework.Rectangle(r.X, r.Y, r.Width, r.Height);

                   // imageSpriteRectangles[k] = src;
                }

                // copy the placement dictionary to the output
                outputMap = imageSpriteRectangles;
                //foreach (var pair in imagePlacement)
                //{
                //    outputMap.Add(pair.Key, pair.Value);
                //}
            }

            // clear our dictionaries just to free up some memory
            bitmaps.Clear();
            imagePlacement.Clear();
        }

        /// <summary>
        /// Returns a Rectangle of the image with transparent borders removed
        /// </summary>
        /// <param name="img"></param>
        /// <returns></returns>
        public Rectangle GetRealRectangle(Bitmap img)
        {
            using (var imgw = img.FastLock())
            {
                Func<int> findLeft = () =>
            {
                for (int x = 0; x < imgw.Width; x++)
                    for (int y = 0; y < imgw.Height; y++)
                    {
                        if (imgw.GetPixel(x, y).A != 0)
                            return Math.Max(x - 1, 0);
                    }

                throw new Exception();
            };

                Func<int> findtop = () =>
                {
                    for (int y = 0; y < imgw.Height; y++)
                        for (int x = 0; x < imgw.Width; x++)

                        {
                            if (imgw.GetPixel(x, y).A != 0)
                                return Math.Max(y - 1, 0);
                        }

                    throw new Exception();
                };
                Func<int> findright = () =>
                {
                    for (int x = imgw.Width - 1; x >= 0; x--)
                        for (int y = 0; y < imgw.Height; y++)
                        {
                            if (imgw.GetPixel(x, y).A != 0)
                                return Math.Min(x + 1, imgw.Width - 1);
                        }

                    throw new Exception();
                };

                Func<int> findbottom = () =>
                {
                    for (int y = imgw.Height - 1; y >= 0; y--)
                        for (int x = 0; x < imgw.Width; x++)

                        {
                            if (imgw.GetPixel(x, y).A != 0)
                                return Math.Min(y + 1, imgw.Height - 1);
                        }

                    throw new Exception();
                };

                try
                {
                    var x = findLeft();
                    var y = findtop();
                    var x2 = findright();
                    var y2 = findbottom();

                    return Rectangle.FromLTRB(x, y, x2, y2);
                }
                catch (Exception ex)
                {

                    return Rectangle.Empty;
                }
            }
        }
        // This method does some trickery type stuff where we perform the TestPackingImages method over and over, 
        // trying to reduce the image size until we have found the smallest possible image we can fit.
        private bool PackImageRectangles()
        {
            // create a dictionary for our test image placements
            Dictionary<string, Rectangle> testImagePlacement = new Dictionary<string, Rectangle>();

            // get the size of our smallest image
            int smallestWidth = int.MaxValue;
            int smallestHeight = int.MaxValue;
            foreach (var size in bitmaps)
            {
                smallestWidth = Math.Min(smallestWidth, size.Value.Width);
                smallestHeight = Math.Min(smallestHeight, size.Value.Height);
            }

            // we need a couple values for testing
            int testWidth = outputWidth;
            int testHeight = outputHeight;

            bool shrinkVertical = false;

            // just keep looping...
            while (true)
            {
                // make sure our test dictionary is empty
                testImagePlacement.Clear();

                // try to pack the images into our current test size
                if (!TestPackingImages(testWidth, testHeight, testImagePlacement))
                {
                    // if that failed...

                    // if we have no images in imagePlacement, i.e. we've never succeeded at PackImages,
                    // show an error and return false since there is no way to fit the images into our
                    // maximum size texture
                    if (imagePlacement.Count == 0)
                        return false;

                    // otherwise return true to use our last good results
                    if (shrinkVertical)
                        return true;

                    shrinkVertical = true;
                    testWidth += smallestWidth + padding + padding;
                    testHeight += smallestHeight + padding + padding;
                    continue;
                }

                // clear the imagePlacement dictionary and add our test results in
                imagePlacement.Clear();
                foreach (var pair in testImagePlacement)
                    imagePlacement.Add(pair.Key, pair.Value);

                // figure out the smallest bitmap that will hold all the images
                testWidth = testHeight = 0;
                foreach (var pair in imagePlacement)
                {
                    testWidth = Math.Max(testWidth, pair.Value.Right);
                    testHeight = Math.Max(testHeight, pair.Value.Bottom);
                }

                // subtract the extra padding on the right and bottom
                if (!shrinkVertical)
                    testWidth -= padding;
                testHeight -= padding;

                // if we require a power of two texture, find the next power of two that can fit this image
                if (requirePow2)
                {
                    testWidth = MiscHelper.FindNextPowerOfTwo(testWidth);
                    testHeight = MiscHelper.FindNextPowerOfTwo(testHeight);
                }

                // if we require a square texture, set the width and height to the larger of the two
                if (requireSquare)
                {
                    int max = Math.Max(testWidth, testHeight);
                    testWidth = testHeight = max;
                }

                // if the test results are the same as our last output results, we've reached an optimal size,
                // so we can just be done
                if (testWidth == outputWidth && testHeight == outputHeight)
                {
                    if (shrinkVertical)
                        return true;

                    shrinkVertical = true;
                }

                // save the test results as our last known good results
                outputWidth = testWidth;
                outputHeight = testHeight;

                // subtract the smallest image size out for the next test iteration
                if (!shrinkVertical)
                    testWidth -= smallestWidth;
                testHeight -= smallestHeight;
            }
        }

        private bool TestPackingImages(int testWidth, int testHeight, Dictionary<string, Rectangle> testImagePlacement)
        {
            // create the rectangle packer
            ArevaloRectanglePacker rectanglePacker = new ArevaloRectanglePacker(testWidth, testHeight);

            foreach (var image in files)
            {
                // get the bitmap for this file
                Size size = bitmaps[image].Size();

                // pack the image
                Point origin;
                if (!rectanglePacker.TryPack(size.Width + padding, size.Height + padding, out origin))
                {
                    return false;
                }

                // add the destination rectangle to our dictionary
                testImagePlacement.Add(image, new Rectangle(origin.X, origin.Y, size.Width + padding, size.Height + padding));
            }

            return true;
        }

        private Bitmap CreateOutputImage()
        {
            //try
            //{
            Bitmap outputImage = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
            using (var output = outputImage.FastLock())
            {
                // draw all the images into the output image
                foreach (var image in files)
                {
                    Rectangle location = imagePlacement[image];
                    Bitmap bitmap = bitmaps[image];
                    if (bitmap == null)
                        return null;

                    // copy pixels over to avoid antialiasing or any other side effects of drawing
                    // the subimages to the output image using Graphics
                    using (var bmp = bitmap.FastLock())
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                            for (int y = 0; y < bitmap.Height; y++)
                                output.SetPixel(location.X + x, location.Y + y, bmp.GetPixel(x, y));
                    }
                }
            }

            return outputImage;
            //}
            //catch
            //{
            //    return null;
            //}
        }
    }
}