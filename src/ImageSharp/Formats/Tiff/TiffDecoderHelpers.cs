// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp.Formats.Experimental.Tiff.Compression;
using SixLabors.ImageSharp.Formats.Experimental.Tiff.Constants;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;

namespace SixLabors.ImageSharp.Formats.Experimental.Tiff
{
    /// <summary>
    /// The decoder helper methods.
    /// </summary>
    internal static class TiffDecoderHelpers
    {
        public static ImageMetadata CreateMetadata(this IList<TiffFrameMetadata> frames, bool ignoreMetadata, TiffByteOrder byteOrder)
        {
            var coreMetadata = new ImageMetadata();
            TiffMetadata tiffMetadata = coreMetadata.GetTiffMetadata();
            tiffMetadata.ByteOrder = byteOrder;

            TiffFrameMetadata rootFrameMetadata = frames.First();
            switch (rootFrameMetadata.ResolutionUnit)
            {
                case TiffResolutionUnit.None:
                    coreMetadata.ResolutionUnits = PixelResolutionUnit.AspectRatio;
                    break;
                case TiffResolutionUnit.Inch:
                    coreMetadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
                    break;
                case TiffResolutionUnit.Centimeter:
                    coreMetadata.ResolutionUnits = PixelResolutionUnit.PixelsPerCentimeter;
                    break;
            }

            if (rootFrameMetadata.HorizontalResolution != null)
            {
                coreMetadata.HorizontalResolution = rootFrameMetadata.HorizontalResolution.Value;
            }

            if (rootFrameMetadata.VerticalResolution != null)
            {
                coreMetadata.VerticalResolution = rootFrameMetadata.VerticalResolution.Value;
            }

            if (!ignoreMetadata)
            {
                foreach (TiffFrameMetadata frame in frames)
                {
                    if (tiffMetadata.XmpProfile == null)
                    {
                        byte[] buf = frame.GetArray<byte>(ExifTag.XMP, true);
                        if (buf != null)
                        {
                            tiffMetadata.XmpProfile = buf;
                        }
                    }

                    if (coreMetadata.IptcProfile == null)
                    {
                        byte[] buf = frame.GetArray<byte>(ExifTag.IPTC, true);
                        if (buf != null)
                        {
                            coreMetadata.IptcProfile = new IptcProfile(buf);
                        }
                    }

                    if (coreMetadata.IccProfile == null)
                    {
                        byte[] buf = frame.GetArray<byte>(ExifTag.IccProfile, true);
                        if (buf != null)
                        {
                            coreMetadata.IccProfile = new IccProfile(buf);
                        }
                    }
                }
            }

            return coreMetadata;
        }

        /// <summary>
        /// Determines the TIFF compression and color types, and reads any associated parameters.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="entries">The IFD entries container to read the image format information for.</param>
        public static void VerifyAndParseOptions(this TiffDecoderCore options, TiffFrameMetadata entries)
        {
            if (entries.ExtraSamples != null)
            {
                TiffThrowHelper.ThrowNotSupported("ExtraSamples is not supported.");
            }

            if (entries.FillOrder != TiffFillOrder.MostSignificantBitFirst)
            {
                TiffThrowHelper.ThrowNotSupported("The lower-order bits of the byte FillOrder is not supported.");
            }

            if (entries.GetArray<uint>(ExifTag.TileOffsets, true) != null)
            {
                TiffThrowHelper.ThrowNotSupported("The Tile images is not supported.");
            }

            if (entries.Predictor == TiffPredictor.FloatingPoint)
            {
                TiffThrowHelper.ThrowNotSupported("ImageSharp does not support FloatingPoint Predictor images.");
            }

            if (entries.SampleFormat != null)
            {
                foreach (TiffSampleFormat format in entries.SampleFormat)
                {
                    if (format != TiffSampleFormat.UnsignedInteger)
                    {
                        TiffThrowHelper.ThrowNotSupported("ImageSharp only supports the UnsignedInteger SampleFormat.");
                    }
                }
            }

            ParseCompression(options, entries.Compression);

            options.PlanarConfiguration = entries.PlanarConfiguration;

            ParsePhotometric(options, entries);

            ParseBitsPerSample(options, entries);

            ParseColorType(options, entries);
        }

        private static void ParseColorType(this TiffDecoderCore options, TiffFrameMetadata entries)
        {
            switch (options.PhotometricInterpretation)
            {
                case TiffPhotometricInterpretation.WhiteIsZero:
                {
                    if (options.BitsPerSample.Length == 1)
                    {
                        switch (options.BitsPerSample[0])
                        {
                            case 8:
                            {
                                options.ColorType = TiffColorType.WhiteIsZero8;
                                break;
                            }

                            case 4:
                            {
                                options.ColorType = TiffColorType.WhiteIsZero4;
                                break;
                            }

                            case 1:
                            {
                                options.ColorType = TiffColorType.WhiteIsZero1;
                                break;
                            }

                            default:
                            {
                                options.ColorType = TiffColorType.WhiteIsZero;
                                break;
                            }
                        }
                    }
                    else
                    {
                        TiffThrowHelper.ThrowNotSupported("The number of samples in the TIFF BitsPerSample entry is not supported.");
                    }

                    break;
                }

                case TiffPhotometricInterpretation.BlackIsZero:
                {
                    if (options.BitsPerSample.Length == 1)
                    {
                        switch (options.BitsPerSample[0])
                        {
                            case 8:
                            {
                                options.ColorType = TiffColorType.BlackIsZero8;
                                break;
                            }

                            case 4:
                            {
                                options.ColorType = TiffColorType.BlackIsZero4;
                                break;
                            }

                            case 1:
                            {
                                options.ColorType = TiffColorType.BlackIsZero1;
                                break;
                            }

                            default:
                            {
                                options.ColorType = TiffColorType.BlackIsZero;
                                break;
                            }
                        }
                    }
                    else
                    {
                        TiffThrowHelper.ThrowNotSupported("The number of samples in the TIFF BitsPerSample entry is not supported.");
                    }

                    break;
                }

                case TiffPhotometricInterpretation.Rgb:
                {
                    if (options.BitsPerSample.Length == 3)
                    {
                        if (options.PlanarConfiguration == TiffPlanarConfiguration.Chunky)
                        {
                            if (options.BitsPerSample[0] == 8 && options.BitsPerSample[1] == 8 && options.BitsPerSample[2] == 8)
                            {
                                options.ColorType = TiffColorType.Rgb888;
                            }
                            else
                            {
                                options.ColorType = TiffColorType.Rgb;
                            }
                        }
                        else
                        {
                            options.ColorType = TiffColorType.RgbPlanar;
                        }
                    }
                    else
                    {
                        TiffThrowHelper.ThrowNotSupported("The number of samples in the TIFF BitsPerSample entry is not supported.");
                    }

                    break;
                }

                case TiffPhotometricInterpretation.PaletteColor:
                {
                    options.ColorMap = entries.ColorMap;
                    if (options.ColorMap != null)
                    {
                        if (options.BitsPerSample.Length == 1)
                        {
                            switch (options.BitsPerSample[0])
                            {
                                default:
                                {
                                    options.ColorType = TiffColorType.PaletteColor;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            TiffThrowHelper.ThrowNotSupported("The number of samples in the TIFF BitsPerSample entry is not supported.");
                        }
                    }
                    else
                    {
                        TiffThrowHelper.ThrowNotSupported("The TIFF ColorMap entry is missing for a palette color image.");
                    }

                    break;
                }

                default:
                {
                    TiffThrowHelper.ThrowNotSupported("The specified TIFF photometric interpretation is not supported: " + options.PhotometricInterpretation);
                }

                break;
            }
        }

        private static void ParseBitsPerSample(this TiffDecoderCore options, TiffFrameMetadata entries)
        {
            options.BitsPerSample = entries.BitsPerSample;
            if (options.BitsPerSample == null)
            {
                if (options.PhotometricInterpretation == TiffPhotometricInterpretation.WhiteIsZero
                    || options.PhotometricInterpretation == TiffPhotometricInterpretation.BlackIsZero)
                {
                    options.BitsPerSample = new[] { (ushort)1 };
                }
                else
                {
                    TiffThrowHelper.ThrowNotSupported("The TIFF BitsPerSample entry is missing.");
                }
            }
        }

        private static void ParsePhotometric(this TiffDecoderCore options, TiffFrameMetadata entries)
        {
            /*
            if (!entries.TryGetSingleNumber(ExifTag.PhotometricInterpretation, out uint photometricInterpretation))
            {
                if (entries.Compression == TiffCompression.Ccitt1D)
                {
                    photometricInterpretation = (uint)TiffPhotometricInterpretation.WhiteIsZero;
                }
                else
                {
                    TiffThrowHelper.ThrowNotSupported("The TIFF photometric interpretation entry is missing.");
                }
            }

            options.PhotometricInterpretation = (TiffPhotometricInterpretation)photometricInterpretation;
            /* */

            // There is no default for PhotometricInterpretation, and it is required.
            options.PhotometricInterpretation = entries.PhotometricInterpretation;
        }

        private static void ParseCompression(this TiffDecoderCore options, TiffCompression compression)
        {
            switch (compression)
            {
                case TiffCompression.None:
                {
                    options.CompressionType = TiffDecoderCompressionType.None;
                    break;
                }

                case TiffCompression.PackBits:
                {
                    options.CompressionType = TiffDecoderCompressionType.PackBits;
                    break;
                }

                case TiffCompression.Deflate:
                case TiffCompression.OldDeflate:
                {
                    options.CompressionType = TiffDecoderCompressionType.Deflate;
                    break;
                }

                case TiffCompression.Lzw:
                {
                    options.CompressionType = TiffDecoderCompressionType.Lzw;
                    break;
                }

                case TiffCompression.CcittGroup3Fax:
                {
                    options.CompressionType = TiffDecoderCompressionType.T4;
                    break;
                }

                case TiffCompression.Ccitt1D:
                {
                    options.CompressionType = TiffDecoderCompressionType.HuffmanRle;
                    break;
                }

                default:
                {
                    TiffThrowHelper.ThrowNotSupported("The specified TIFF compression format is not supported: " + compression);
                    break;
                }
            }
        }
    }
}