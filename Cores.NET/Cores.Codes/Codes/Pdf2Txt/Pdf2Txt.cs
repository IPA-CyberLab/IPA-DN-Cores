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
using System.Text.RegularExpressions;
using System.Globalization;

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

    /// <summary>
    /// PDF の Date 文字列（例: "D:19981223195200-08'00'"）
    /// を DateTimeOffset に変換する
    /// </summary>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="FormatException"/>
    public static DateTimeOffset PdfDateTimeStringToDtOffset(string pdfDateString)
    {
        try
        {
            return PdfDateTimeStringToDtOffsetCore(pdfDateString);
        }
        catch
        {
            return ZeroDateTimeOffsetValue;
        }
    }
    // ──主要パターンを 1 本の Regex で吸収──
    private static readonly Regex _rx = new(
        @"^D?:?(?<year>\d{4})" +
        @"(?<month>\d{2})?" +
        @"(?<day>\d{2})?" +
        @"(?<hour>\d{2})?" +
        @"(?<minute>\d{2})?" +
        @"(?<second>\d{2})?" +
        @"(?<offset>Z|[+\-]\d{2}('?[:]?\d{2})?'?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// PDF 日時文字列 → <see cref="DateTimeOffset"/> へ変換します。
    /// 解析できない場合は <see cref="FormatException"/> を送出します。
    /// </summary>
    static DateTimeOffset PdfDateTimeStringToDtOffsetCore(string pdfDateString)
    {
        if (pdfDateString is null) throw new ArgumentNullException(nameof(pdfDateString));

        // () や D: を除去
        pdfDateString = pdfDateString.Trim();
        if (pdfDateString.StartsWith("(") && pdfDateString.EndsWith(")"))
            pdfDateString = pdfDateString[1..^1];
        if (pdfDateString.StartsWith("D:")) pdfDateString = pdfDateString[2..];

        var m = _rx.Match(pdfDateString);
        if (!m.Success)
            throw new FormatException($"'{pdfDateString}' is not a valid PDF date string.");

        // 欠損フィールドは既定値
        int year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture);
        int month = m.Groups["month"].Success ? int.Parse(m.Groups["month"].Value) : 1;
        int day = m.Groups["day"].Success ? int.Parse(m.Groups["day"].Value) : 1;
        int hour = m.Groups["hour"].Success ? int.Parse(m.Groups["hour"].Value) : 0;
        int minute = m.Groups["minute"].Success ? int.Parse(m.Groups["minute"].Value) : 0;
        int second = m.Groups["second"].Success ? int.Parse(m.Groups["second"].Value) : 0;

        var localDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        var offset = ParseOffset(m.Groups["offset"].Value, localDateTime);

        return new DateTimeOffset(localDateTime, offset);
    }

    /// <summary>タイムゾーン部を TimeSpan に変換</summary>
    private static TimeSpan ParseOffset(string raw, DateTime baseDate)
    {
        if (string.IsNullOrEmpty(raw))
            return TimeZoneInfo.Local.GetUtcOffset(baseDate);      // 明示なし＝ローカル

        if (raw.Equals("Z", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.Zero;

        // "+09'00'", "+0900", "+09:00" 等を統一
        string cleaned = raw.Replace("'", "").Replace(":", "");
        int sign = cleaned[0] == '-' ? -1 : 1;
        int hours = int.Parse(cleaned[1..3]);
        int minutes = cleaned.Length > 3 ? int.Parse(cleaned[3..5]) : 0;

        return TimeSpan.FromMinutes(sign * (hours * 60 + minutes));
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

/// <summary>
/// 各ページのサイズ情報（mm単位）を格納するデータクラス
/// </summary>
public class PdfPageInfo
{
    /// <summary>1始まりのページ番号</summary>
    public int PageNumber1Origin { get; set; }
    /// <summary>幅 (mm)</summary>
    public double WidthMm { get; set; }
    /// <summary>高さ (mm)</summary>
    public double HeightMm { get; set; }
}

public static class PdfPageInfoLib
{
    // ポイント(pt)をミリメートル(mm)に変換する定数
    private const double PointToMillimeter = 25.4 / 72.0;

    /// <summary>
    /// PDFファイルのページ数を取得する
    /// </summary>
    /// <param name="pdfPath">PDFファイルのパス</param>
    /// <returns>ページ数</returns>
    public static int GetPdfNumPaged(string pdfPath)
    {
        // PdfDocument.Open は内部で最低限の情報だけを読み込むため高速
        using (var document = PdfDocument.Open(pdfPath))
        {
            return document.NumberOfPages;
        }
    }

    /// <summary>
    /// PDFファイルの各ページサイズをミリメートル単位で取得する
    /// </summary>
    /// <param name="pdfPath">PDFファイルのパス</param>
    /// <returns>各ページの PageSizeMm リスト</returns>
    public static List<PdfPageInfo> GetPdfPageInfo(string pdfPath)
    {
        var sizes = new List<PdfPageInfo>();

        using (var document = PdfDocument.Open(pdfPath))
        {
            // GetPages() は内部でページツリーをイテレートするだけ
            foreach (var page in document.GetPages())
            {
                double widthPt = page.Width;
                double heightPt = page.Height;

                sizes.Add(new PdfPageInfo
                {
                    PageNumber1Origin = page.Number,
                    WidthMm = Math.Round(widthPt * PointToMillimeter, 2),
                    HeightMm = Math.Round(heightPt * PointToMillimeter, 2),
                });
            }
        }

        return sizes;
    }

    /// <summary>
    /// 余白トリム後の「でこぼこ PDF」（種類 2）と推定される場合 true。
    /// 紙をただスキャンしただけ（種類 1）または判定不能の場合は false。
    /// </summary>
    public static bool DetermineIsDekobokoPdf(string pdfPath)
        => DetermineIsDekobokoPdf(GetPdfPageInfo(pdfPath));

    /// <summary>
    /// 余白トリムによりページサイズが「でこぼこ」かどうか推定する。
    /// true なら <種類 2>（トリム済み）、false なら <種類 1>（スキャンそのまま）または判定不能。
    /// </summary>
    public static bool DetermineIsDekobokoPdf(List<PdfPageInfo> pageInfoList)
    {
        if (pageInfoList == null || pageInfoList.Count < 2)
            return false;                     // ページが 0～1 枚なら判断できないので安全側

        const double TOLERANCE_RATIO = 0.04;  // 4% までを「許容ゆらぎ」とみなす
        int totalPages = pageInfoList.Count;
        int dekobokoCount = 0;

        // 先頭ページの面積を基準に走査
        double prevArea = pageInfoList[0].WidthMm * pageInfoList[0].HeightMm;

        for (int i = 1; i < totalPages; i++)
        {
            var page = pageInfoList[i];
            double area = page.WidthMm * page.HeightMm;
            double ratio = Math.Abs(area - prevArea) / prevArea;

            if (ratio > TOLERANCE_RATIO)
                dekobokoCount++;

            prevArea = area;  // 次比較用に更新
        }

        // 「でこぼこ発生」がページ数の一定割合以上なら <種類 2> と推定
        return dekobokoCount >= totalPages * 0.03;
    }
}

#endif
