﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Silk.NET.Maths;

namespace CHPEditor
{
    internal class CHPFile : IDisposable
    {
        public enum ColorKeyType
        {
            None = 0,
            Manual = 1,
            Auto = 2
        }
        public bool Loaded { get; private set; }
        public string Error { get; private set; }

        public string FileName;
        public string FilePath;
        public string FolderPath
        {
            get
            {
                return Path.GetDirectoryName(FilePath);
            }
        }
        public Encoding FileEncoding { get; private set; }

        public int Anime = 83; // I don't remember where I got this number from, but I assume it's the correct one. Doesn't look off to me.
        public int[] Size = [121, 271]; // Begin with 121,271 for legacy support. Modern pomyus are typically 167,271.
        public int Wait = 1;
        public int Data = 16; // Required for hexadecimal conversion
        public bool AutoColorSet = false;
        public Rectangle<int> CharFaceUpperSize = new Rectangle<int>(0, 0, 256, 256);
        public Rectangle<int> CharFaceAllSize = new Rectangle<int>(320, 0, 320, 480);

        public bool IsLegacy { get; private set; } = true;

        public struct AnimeData
        {
            public bool Loaded;
            public int Frame;
            public int FrameCount;
            public int Loop;
            public int[] Pattern;
            public List<int[][]> Layer;
            public List<int[][]> Texture;
        }
        public struct InterpolateData // type -> key -> interarray: start, length, startpos, endpos
        {
            public List<int[][][]> Layer;
            public List<int[][][]> Texture;
        }
        public struct BitmapData
        {
            public string Path;
            public ImageFileManager? ImageFile;
            public ColorKeyType ColorKeyType;
            public Color ColorKey;
            public Vector2D<int> Bounds
            { 
                get
                {
                    if (Loaded)
                        return new Vector2D<int>(ImageFile.Image.Width, ImageFile.Image.Height);
                    else
                        return new Vector2D<int>(0, 0);
                }
            }
            public bool IsBMPFile
            {
                get
                {   
                    return System.IO.Path.GetExtension(Path) == ".bmp";
                }
            }
            public bool Loaded
            {
                get
                {
                    if (ImageFile == null)
                        return false;
                    return ImageFile.Loaded;
                }
            }
        }

        public string CharName,
            Artist;
        public string CharFile { get; protected set; }
        public BitmapData CharBMP,
            CharBMP2P,
            CharFace,
            CharFace2P,
            SelectCG,
            SelectCG2P,
            CharTex,
            CharTex2P;

        public Rectangle<int>[] RectCollection;

        public AnimeData[] AnimeCollection { get; protected set; }
        public InterpolateData[] InterpolateCollection { get; protected set; }
        public CHPFile(string filename)
        {
            Loaded = false;
            Error = "";

            try
            {
                FileEncoding = HEncodingDetector.DetectEncoding(filename, Encoding.GetEncoding(932));

                string filedata = File.ReadAllText(filename, FileEncoding);
                FileName = Path.GetFileName(filename);
                FilePath = filename;

                AnimeCollection = new AnimeData[18];
                InterpolateCollection = new InterpolateData[18];

                filedata = filedata.Replace("\r\n", "\n");
                string[] lines = filedata.Split("\n");
                foreach (string line in lines)
                {
                    if (line.StartsWith("/") || line.StartsWith("//") || string.IsNullOrWhiteSpace(line))
                        continue;
                    else
                    {
                        // parsing time :)
                        string line_trimmed = line.Substring(0, line.IndexOf("//") > -1 ? line.IndexOf("//") : line.Length);
                        string[] split = line_trimmed.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        switch (split[0].ToLower())
                        {
                            #region Chara name & artist
                            case "#charname":
                                CharName = SquashArray(split, 2)[1];
                                break;

                            case "#artist":
                                Artist = SquashArray(split, 2)[1];
                                break;
                            #endregion
                            #region Bitmaps
                            case "#charbmp":
                                LoadTexture(ref CharBMP, SquashArray(split, 2)[1]);
                                break;

                            case "#charbmp2p":
                                LoadTexture(ref CharBMP2P, SquashArray(split, 2)[1]);
                                break;

                            case "#charface":
                                LoadTexture(ref CharFace, SquashArray(split, 2)[1], ColorKeyType.Manual, 0, 0, 0);
                                break;

                            case "#charface2p":
                                LoadTexture(ref CharFace2P, SquashArray(split, 2)[1], ColorKeyType.Manual, 0, 0, 0);
                                break;

                            case "#selectcg":
                                string cgfile = SquashArray(split, 2)[1];

                                LoadTexture(ref SelectCG, cgfile, 0);
                                break;

                            case "#selectcg2p": // Added in beatoraja
                                string cgfile2 = SquashArray(split, 2)[1];

                                LoadTexture(ref SelectCG2P, cgfile2, 0);
                                break;

                            case "#chartex":
                                LoadTexture(ref CharTex, SquashArray(split, 2)[1]);
                                break;

                            case "#chartex2p":
                                LoadTexture(ref CharTex2P, SquashArray(split, 2)[1]);
                                break;
                            #endregion
                            #region Chara parameters
                            case "#autocolorset":
                                AutoColorSet = true;
                                break;

                            case "#anime":
                                if (!int.TryParse(split[1], out Anime))
                                    Trace.TraceError($"Failed to parse Anime value. \"{split[1]}\" was not recognized as an integer. Did you write it correctly?");
                                break;

                            case "#size":
                                if (!int.TryParse(split[1], out Size[0]))
                                    Trace.TraceError($"Failed to parse Size width value. \"{split[1]}\" was not recognized as an integer. Did you write it correctly?");
                                if (!int.TryParse(split[2], out Size[1]))
                                    Trace.TraceError($"Failed to parse Size height value. \"{split[2]}\" was not recognized as an integer. Did you write it correctly?");
                                break;

                            case "#wait":
                                if (!int.TryParse(split[1], out Wait))
                                    Trace.TraceError($"Failed to parse Wait value. \"{split[1]}\" was not recognized as an integer. Did you write it correctly?");
                                break;

                            case "#data":
                                if (!int.TryParse(split[1], out Data))
                                    Trace.TraceError($"Failed to parse Data value. \"{split[1]}\" was not recognized as an integer. Did you write it correctly?");
                                IsLegacy = false;
                                break;
                            
                            case "#charfaceallsize": // Added in beatoraja
                                if (split.Length >= 5)
                                {
                                    CharFaceAllSize.Origin.X = int.TryParse(split[1], out int x) ? x : CharFaceAllSize.Origin.X;
                                    CharFaceAllSize.Origin.Y = int.TryParse(split[2], out int y) ? y : CharFaceAllSize.Origin.Y;
                                    CharFaceAllSize.Size.X = int.TryParse(split[3], out int w) ? w : CharFaceAllSize.Size.X;
                                    CharFaceAllSize.Size.Y = int.TryParse(split[4], out int h) ? h : CharFaceAllSize.Size.Y;
                                }
                                else
                                    Trace.TraceWarning($"#CharFaceAllSize could not be parsed. Found {split.Length - 1} values instead of 4. Using default values instead.");
                                break;
                            
                            case "#charfaceuppersize": // Added in beatoraja
                                if (split.Length >= 5)
                                {
                                    CharFaceUpperSize.Origin.X = int.TryParse(split[1], out int x) ? x : CharFaceUpperSize.Origin.X;
                                    CharFaceUpperSize.Origin.Y = int.TryParse(split[2], out int y) ? y : CharFaceUpperSize.Origin.Y;
                                    CharFaceUpperSize.Size.X = int.TryParse(split[3], out int w) ? w : CharFaceUpperSize.Size.X;
                                    CharFaceUpperSize.Size.Y = int.TryParse(split[4], out int h) ? h : CharFaceUpperSize.Size.Y;
                                }
                                else
                                    Trace.TraceWarning($"#CharFaceUpperSize could not be parsed. Found {split.Length - 1} values instead of 4. Using default values instead.");
                                break;
                            #endregion
                            #region Animation
                            case "#loop":
                                int loop = int.Parse(split[1]) - 1;

                                AnimeCollection[loop].Loop = int.Parse(split[2]);
                                break;

                            case "#flame": // This is the correct command, it's just a misspelling that ended up being final.
                            case "#frame": // Added in beatoraja
                                int flame = int.Parse(split[1]) - 1;
                                AnimeCollection[flame].Frame = int.Parse(split[2]);
                                break;

                            case "#patern": // This is also the correct command, but was once again misspelled.
                            case "#pattern": // Added in beatoraja
                                int patern = int.Parse(split[1]) - 1;

                                AnimeCollection[patern].Pattern = new int[split[2].Length / 2];
                                for (int i = 0; i < AnimeCollection[patern].Pattern.Length; i++)
                                    if (int.TryParse(split[2].Substring(i * 2, 2), NumberStyles.HexNumber, null, out int result))
                                        AnimeCollection[patern].Pattern[i] = result;
                                    else
                                        AnimeCollection[patern].Pattern[i] = -1; // Indicator of interpoling point


                                AnimeCollection[patern].Loaded = true;
                                break;

                            case "#texture":
                                int texture = int.Parse(split[1]) - 1;

                                if (AnimeCollection[texture].Texture == null)
                                {
                                    AnimeCollection[texture].Texture = new List<int[][]>();
                                    InterpolateCollection[texture].Texture = new List<int[][][]>();
                                }
                                AnimeCollection[texture].Texture.Add(new int[split[2].Length / 2][]);
                                InterpolateCollection[texture].Texture.Add(new int[4][][]);

                                for (int i = 0; i < AnimeCollection[texture].Texture.Last().Length; i++)
                                {
                                    AnimeCollection[texture].Texture.Last()[i] = new int[4] { 0, -1, 255, 0 };

                                    for (int j = 0; j < 4 && j < split.Length - 2; j++)
                                        if (j + 2 < split.Length)
                                        {
                                            if (int.TryParse(split[j + 2].Substring(i * 2, 2), NumberStyles.HexNumber, null, out int result))
                                            {
                                                AnimeCollection[texture].Texture.Last()[i][j] = result;
                                                #region Interpolate
                                                if (i + 1 < AnimeCollection[texture].Texture.Last().Length)
                                                {
                                                    if (!int.TryParse(split[j + 2].Substring((i + 1) * 2, 2), NumberStyles.HexNumber, null, out int interresult))
                                                    {
                                                        if (InterpolateCollection[texture].Texture.Last()[j] == null)
                                                        {
                                                            InterpolateCollection[texture].Texture.Last()[j] = new int[1][];
                                                            InterpolateCollection[texture].Texture.Last()[j][0] = new int[4] {
                                                                AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame * i : Anime * i,
                                                                AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame : Anime,
                                                                result,
                                                                0
                                                            };
                                                        }
                                                        else
                                                        {
                                                            Array.Resize(ref InterpolateCollection[texture].Texture.Last()[j], InterpolateCollection[texture].Texture.Last()[j].Length + 1);
                                                            InterpolateCollection[texture].Texture.Last()[j][InterpolateCollection[texture].Texture.Last()[j].Length - 1] = new int[4] {
                                                                AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame * i : Anime * i,
                                                                AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame : Anime,
                                                                result,
                                                                0
                                                            };
                                                        }
                                                    }
                                                    else if (InterpolateCollection[texture].Texture.Last()[j] != null && i - 1 > 0)
                                                    {
                                                        if (!int.TryParse(split[j + 2].Substring((i - 1) * 2, 2), NumberStyles.HexNumber, null, out int _))
                                                            InterpolateCollection[texture].Texture.Last()[j][InterpolateCollection[texture].Texture.Last()[j].Length - 1][1] +=
                                                                AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame : Anime;
                                                    }
                                                }
                                                else if (InterpolateCollection[texture].Texture.Last()[j] != null && i - 1 > 0)
                                                {
                                                    if (!int.TryParse(split[j + 2].Substring((i - 1) * 2, 2), NumberStyles.HexNumber, null, out int _))
                                                        InterpolateCollection[texture].Texture.Last()[j][InterpolateCollection[texture].Texture.Last()[j].Length - 1][1] +=
                                                            AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame : Anime;
                                                }
                                                #endregion
                                            }
                                            else
                                            {
                                                AnimeCollection[texture].Texture.Last()[i][j] = -1; // Indicator of interpoling point
                                                #region Interpolate
                                                InterpolateCollection[texture].Texture.Last()[j].Last()[1] += AnimeCollection[texture].Frame != 0 ? AnimeCollection[texture].Frame : Anime;
                                                if (i + 1 < AnimeCollection[texture].Texture.Last().Length)
                                                {
                                                    if (int.TryParse(split[j + 2].Substring((i + 1) * 2, 2), NumberStyles.HexNumber, null, out int interresult))
                                                    {
                                                        InterpolateCollection[texture].Texture.Last()[j].Last()[3] = interresult;
                                                    }
                                                }
                                                #endregion
                                            }
                                        }

                                }

                                AnimeCollection[texture].Loaded = true;
                                break;

                            case "#layer":
                                int layer = int.Parse(split[1]) - 1;

                                if (AnimeCollection[layer].Layer == null)
                                {
                                    AnimeCollection[layer].Layer = new List<int[][]>();
                                    InterpolateCollection[layer].Layer = new List<int[][][]>();
                                }
                                AnimeCollection[layer].Layer.Add(new int[split[2].Length / 2][]);
                                InterpolateCollection[layer].Layer.Add(new int[2][][]);

                                for (int i = 0; i < AnimeCollection[layer].Layer.Last().Length; i++)
                                {
                                    AnimeCollection[layer].Layer.Last()[i] = new int[2] { 0, -1 };
                                    for (int j = 0; j < 2 && j < split.Length - 2; j++)
                                        if (int.TryParse(split[j + 2].Substring(i * 2, 2), NumberStyles.HexNumber, null, out int result))
                                        {
                                            AnimeCollection[layer].Layer.Last()[i][j] = result;
                                            #region Interpolate
                                            if (i + 1 < AnimeCollection[layer].Layer.Last().Length)
                                            {
                                                if (!int.TryParse(split[j + 2].Substring((i + 1) * 2, 2), NumberStyles.HexNumber, null, out int _))
                                                {
                                                    if (InterpolateCollection[layer].Layer.Last()[j] == null)
                                                    {
                                                        InterpolateCollection[layer].Layer.Last()[j] = new int[1][];
                                                        InterpolateCollection[layer].Layer.Last()[j][0] = new int[4] {
                                                                AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame * i : Anime * i,
                                                                AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame : Anime,
                                                                result,
                                                                0
                                                            };
                                                    }
                                                    else
                                                    {
                                                        Array.Resize(ref InterpolateCollection[layer].Layer.Last()[j], InterpolateCollection[layer].Layer.Last()[j].Length + 1);
                                                        InterpolateCollection[layer].Layer.Last()[j][InterpolateCollection[layer].Layer.Last()[j].Length - 1] = new int[4] {
                                                                AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame * i : Anime * i,
                                                                AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame : Anime,
                                                                result,
                                                                0
                                                            };
                                                    }
                                                }
                                                else if (InterpolateCollection[layer].Layer.Last()[j] != null && i - 1 > 0)
                                                {
                                                    if (!int.TryParse(split[j + 2].Substring((i - 1) * 2, 2), NumberStyles.HexNumber, null, out int _))
                                                        InterpolateCollection[layer].Layer.Last()[j][InterpolateCollection[layer].Layer.Last()[j].Length - 1][1] +=
                                                            AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame : Anime;
                                                }
                                            }
                                            else if (InterpolateCollection[layer].Layer.Last()[j] != null && i - 1 > 0)
                                            {
                                                if (!int.TryParse(split[j + 2].Substring((i - 1) * 2, 2), NumberStyles.HexNumber, null, out int _))
                                                    InterpolateCollection[layer].Layer.Last()[j][InterpolateCollection[layer].Layer.Last()[j].Length - 1][1] +=
                                                        AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame : Anime;
                                            }
                                            #endregion
                                        }
                                        else
                                        { 
                                            AnimeCollection[layer].Layer.Last()[i][j] = -1; // Indicator of interpoling point
                                            #region Interpolate
                                            InterpolateCollection[layer].Layer.Last()[j].Last()[1] += AnimeCollection[layer].Frame != 0 ? AnimeCollection[layer].Frame : Anime;
                                            if (i + 1 < AnimeCollection[layer].Layer.Last().Length)
                                            {
                                                if (int.TryParse(split[j + 2].Substring((i + 1) * 2, 2), NumberStyles.HexNumber, null, out int interresult))
                                                {
                                                    InterpolateCollection[layer].Layer.Last()[j].Last()[3] = interresult;
                                                }
                                            }
                                            #endregion
                                        }
                                }

                                AnimeCollection[layer].Loaded = true;
                                break;
                            #endregion
                            default: // Remember that #00 and #01 are strictly reserved for Name Logo & character background respectively
                                if (RectCollection == null)
                                {
                                    RectCollection = new Rectangle<int>[Data * Data];
                                    for (int i = 0; i < RectCollection.Length; i++)
                                        RectCollection[i] = new Rectangle<int>(0,0,0,0);
                                }
                                if (IsLegacy && int.TryParse(split[0].Substring(1, 2), NumberStyles.Integer, null, out int number))
                                {
                                    if (split.Length >= 2 && int.TryParse(split[1], out int x))
                                        RectCollection[number].Origin.X = x;
                                    if (split.Length >= 3 && int.TryParse(split[2], out int y))
                                        RectCollection[number].Origin.Y = y;
                                    if (split.Length >= 4 && int.TryParse(split[3], out int w))
                                        RectCollection[number].Size.X = w;
                                    if (split.Length >= 5 && int.TryParse(split[4], out int h))
                                        RectCollection[number].Size.Y = h;
                                }
                                else if (int.TryParse(split[0].Substring(1, 2), NumberStyles.HexNumber, null, out int number_from_hex))
                                {
                                    if (IsLegacy)
                                    {
                                        Rectangle<int>[] newRect = new Rectangle<int>[Data * Data];
                                        for (int i = 0; i < RectCollection.Length; i++)
                                        {
                                            if (RectCollection[i] != new Rectangle<int>(0, 0, 0, 0))
                                                newRect[int.Parse(i.ToString(), NumberStyles.HexNumber)] = RectCollection[i];
                                        }
                                        RectCollection = newRect;
                                        IsLegacy = false;
                                    }

                                    if (split.Length >= 2 && int.TryParse(split[1], out int x))
                                        RectCollection[number_from_hex].Origin.X = x;
                                    if (split.Length >= 3 && int.TryParse(split[2], out int y))
                                        RectCollection[number_from_hex].Origin.Y = y;
                                    if (split.Length >= 4 && int.TryParse(split[3], out int w))
                                        RectCollection[number_from_hex].Size.X = w;
                                    if (split.Length >= 5 && int.TryParse(split[4], out int h))
                                        RectCollection[number_from_hex].Size.Y = h;
                                }
                                break;
                        }
                    }
                }
                for (int i = 0; i < AnimeCollection.Length; i++)
                {
                    List<int> all_lengths = new List<int>();

                    if (AnimeCollection[i].Pattern != null)
                    {
                        all_lengths.Add(AnimeCollection[i].Pattern.Length);
                    }
                    if (AnimeCollection[i].Texture != null)
                    {
                        foreach (int[][] tex in AnimeCollection[i].Texture)
                        {
                            all_lengths.Add(tex.Length);
                        }
                    }
                    if (AnimeCollection[i].Layer != null)
                    {
                        foreach (int[][] layer in AnimeCollection[i].Layer)
                        {
                            all_lengths.Add(layer.Length);
                        }
                    }

                    AnimeCollection[i].FrameCount = all_lengths.Count > 0 ? all_lengths.Min() : 0;

                    if (all_lengths.Distinct().Count() > 1)
                        Trace.TraceWarning("State #" + (i+1) + " contains differing amounts of frames.\n" +
                            "This may break applications that try to display Pomyu Charas.\n" +
                            "Only the minimum amount of frames (" + all_lengths.Min() + ") will be displayed here.\n" +
                            "Frame Counts: " + (string.Join(",", all_lengths.Select(len => len.ToString()).ToArray())));
                }
                Loaded = true;
                Trace.TraceInformation("Pomyu Chara loaded! (Legacy: " + IsLegacy + ")");
                Trace.TraceInformation("File Name: " + FileName);
                Trace.TraceInformation("Chara Name: " + CharName);
                Trace.TraceInformation("Artist: " + Artist);
            }
            catch (FileNotFoundException e)
            {
                string err = "Couldn't find the requested file. More details:" + Environment.NewLine + e;
                Trace.TraceError(err);
                Error = err;
            }
            catch (Exception e)
            {
                string err = "Something went wrong while trying to read the requested CHP file. More details:" + Environment.NewLine + e;
                Trace.TraceError(err);
                Error = err;
            }
        }
        
        private unsafe void LoadTexture(ref BitmapData data, string filepath, ColorKeyType colorKey = ColorKeyType.Auto, byte r = 0x00, byte g = 0x00, byte b = 0x00, byte a = 0xFF)
        {
            data.Path = filepath;
            data.ImageFile = new ImageFileManager(GetPath(data.Path), true);
            data.ColorKeyType = colorKey;

            if (colorKey == ColorKeyType.Auto && data.ImageFile.Loaded)
            {
                int offset = ((data.ImageFile.Image.Width * data.ImageFile.Image.Height) - 1) * 4;
                data.ColorKey = Color.FromArgb(data.ImageFile.Image.Data[offset + 3], data.ImageFile.Image.Data[offset], data.ImageFile.Image.Data[offset + 1], data.ImageFile.Image.Data[offset + 2]);
            }
            else if (colorKey == ColorKeyType.Manual && data.ImageFile.Loaded)
            {
                data.ColorKey = Color.FromArgb(a,r,g,b);
            }
            else
            {
                data.ColorKey = Color.FromArgb(0x00,0x00,0x00,0x00);
            }
        }

        private string GetPath(string path)
        {
            return Path.Combine(FolderPath, path);
        }

        private string[] SquashArray(string[] array, int size, char joiner = ' ')
        {
            for (int i = size; i < array.Length; i++)
            {
                array[size - 1] += joiner + array[i];
            }
            return array;
        }

        #region Dispose
        private bool isDisposed;
        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    Loaded = false;
                    Error = "";

                    FileName = "";
                    FilePath = "";
                    FileEncoding = null;
                    CharName = "";
                    Artist = "";
                    CharFile = "";
                    RectCollection = [];
                    AnimeCollection = [];
                    InterpolateCollection = [];
                }

                CharBMP.ImageFile?.Dispose();
                CharBMP2P.ImageFile?.Dispose();
                CharFace.ImageFile?.Dispose();
                CharFace2P.ImageFile?.Dispose();
                SelectCG.ImageFile?.Dispose();
                SelectCG2P.ImageFile?.Dispose();
                CharTex.ImageFile?.Dispose();
                CharTex2P.ImageFile?.Dispose();

                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
