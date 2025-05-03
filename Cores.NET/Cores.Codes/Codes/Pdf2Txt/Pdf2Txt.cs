// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

// Author: Daiyuu Nobori
// Description

// <PackageReference Include="PdfPig" Version="0.1.9" />

#if CORES_CODES_PDF2TXT

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

namespace IPA.Cores.Codes;

public static class Pdf2Txt
{
    public const int DefaultMaxPdfFileSize = 2_100_000_000;

    public static async Task<PdfDocument> LoadPdfFromFileAsync(string fileName, int maxSize = DefaultMaxPdfFileSize, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default)
    {

        fs ??= Lfs;

        var data = await fs.ReadDataFromFileAsync(fileName, maxSize, flags, cancel);

        return LoadPdf(data);
    }
    public static PdfDocument LoadPdfFromFile(string fileName, int maxSize = DefaultMaxPdfFileSize, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default)
        => LoadPdfFromFileAsync(fileName, maxSize, flags, fs, cancel)._GetResult();

    public static PdfDocument LoadPdf(ReadOnlyMemory<byte> pdfBody)
    {
        return PdfDocument.Open(pdfBody.ToArray());
    }

    public static byte[] SavePdf_Shitagaki(this PdfDocument pdf)
    {
        using var builder = new PdfDocumentBuilder();

        foreach (var page in pdf.GetPages())
        {
            builder.AddPage(pdf, page.Number);
        }

        builder.DocumentInformation.CustomMetadata._PrintAsJson();

        return builder.Build();
    }

    public static async Task<string> ExtraceTextFromPdfAsync(FilePath filePath, int maxSize = DefaultMaxPdfFileSize, CancellationToken cancel = default)
    {
        using var pdf = await LoadPdfFromFileAsync(filePath, flags: filePath.Flags, fs: filePath.FileSystem, cancel: cancel);

        return pdf.ExtractTextFromPdf();
    }
    public static Task<string> ExtraceTextFromPdfAsync(string filePath, int maxSize = DefaultMaxPdfFileSize, CancellationToken cancel = default)
        => ExtraceTextFromPdfAsync(new FilePath(filePath), maxSize, cancel);

    public static string ExtractTextFromPdf(this PdfDocument pdf)
    {
        StringWriter w = new StringWriter();

        foreach (var page in pdf.GetPages())
        {
            string a = page.ExtractTextFromPage();

            w.WriteLine(a);
        }

        return w.ToString();
    }

    public static string ExtractTextFromPage(this Page page)
    {
        string a = page.Text;

        return a;
    }

    public static bool CalcHasPdfText(this PdfDocument pdf)
    {
        int num = 0;

        foreach (var page in pdf.GetPages())
        {
            string a = page.ExtractTextFromPage();

            if (a.Length >= 16)
            {
                num++;
            }
        }

        return num >= Math.Max((pdf.NumberOfPages / 10), 4);
    }

    public static bool CalcIsPdfVertical(this PdfDocument pdf)
    {
        int numVertical = 0;

        foreach (var page in pdf.GetPages())
        {
            if (CalcIsPageVertical(page))
            {
                numVertical++;
            }
        }

        if (numVertical >= (pdf.NumberOfPages / 2))
        {
            return true;
        }

        return false;
    }

    public static bool CalcIsPageVertical(this Page page)
    {
        var letters = page.Letters;

        int count = letters.Count;

        if (count <= 10)
        {
            return false;
        }

        double totalX = 0, totalY = 0;

        for (int i = 1; i < count - 1; i++)
        {
            Letter prev = letters[i - 1];
            Letter cur = letters[i];

            double relativeX = cur.GlyphRectangle.Left - prev.GlyphRectangle.Left;
            double relativeY = cur.GlyphRectangle.Top - prev.GlyphRectangle.Top;

            relativeX = Math.Abs(relativeX);
            relativeY = Math.Abs(relativeY);

            totalX += relativeX;
            totalY += relativeY;
        }

        if (totalY > totalX)
        {
            return true;
        }

        return false;
    }
}

public static class Pdf2TxtApp
{
    public static void CopyAllNonOcrPdfFiles(string srcDir, string destDir)
    {
        var srcPdfFiles = Lfs.EnumDirectory(srcDir, true, wildcard: "*.pdf");

        foreach (var srcPdfFile in srcPdfFiles)
        {
            try
            {
                string relativeFileName = PP.GetRelativeFileName(srcPdfFile.FullPath, srcDir);

                $"Loading '{relativeFileName}' ..."._Print();

                using var pdfDoc = Pdf2Txt.LoadPdf(Lfs.ReadDataFromFile(srcPdfFile.FullPath));

                bool hasText = pdfDoc.CalcHasPdfText();

                if (hasText == false)
                {
                    string destFullPath = PP.Combine(destDir, relativeFileName);

                    $"   Copying..."._Print();
                    Lfs.CopyFile(srcPdfFile.FullPath, destFullPath, new CopyFileParams(flags: FileFlags.AutoCreateDirectory, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll)));
                }
            }
            catch (Exception ex)
            {
                srcPdfFile.FullPath._Error();
                ex._Error();
            }
        }
    }
}

#endif

