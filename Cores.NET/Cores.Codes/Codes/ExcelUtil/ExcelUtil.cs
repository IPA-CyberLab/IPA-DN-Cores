﻿// IPA Cores.NET
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

#if CORES_CODES_EXCELUTIL

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

using OfficeOpenXml;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

namespace IPA.Cores.Codes;

public static class ExcelFileSystemExtensions
{
    public static async Task<ExcelUtil> OpenExcelReadWriteAsync(this FileSystem fs, string path, int maxSize = int.MaxValue, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        var file = await fs.OpenAsync(path, writeMode: true, noShare: noShare, readLock: readLock, flags: flags, cancel: cancel);
        try
        {
            var stream = file.GetStream(true);
            try
            {
                ExcelUtil excel = new ExcelUtil(stream);

                return excel;
            }
            catch
            {
                throw;
            }
        }
        catch
        {
            await file._DisposeSafeAsync();
            throw;
        }
    }
    public static ExcelUtil OpenExcelReadWrite(this FileSystem fs, string path, int maxSize = int.MaxValue, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => OpenExcelReadWriteAsync(fs, path, maxSize, noShare, readLock, flags, cancel)._GetResult();

    public static async Task<ExcelUtil> OpenExcelReadOnlyAsync(this FileSystem fs, string path, int maxSize = int.MaxValue, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
    {
        var file = await fs.OpenAsync(path, writeMode: false, noShare: noShare, readLock: readLock, flags: flags, cancel: cancel);
        try
        {
            var stream = file.GetStream(true);
            try
            {
                ExcelUtil excel = new ExcelUtil(stream);

                return excel;
            }
            catch
            {
                throw;
            }
        }
        catch
        {
            await file._DisposeSafeAsync();
            throw;
        }
    }
    public static ExcelUtil OpenExcelReadOnly(this FileSystem fs, string path, int maxSize = int.MaxValue, bool noShare = false, bool readLock = false, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => OpenExcelReadOnlyAsync(fs, path, maxSize, noShare, readLock, flags, cancel)._GetResult();
}

public class ExcelUtil : AsyncService
{
    public ExcelPackage Excel { get; }

    public ExcelWorkbook Workbook => this.Excel.Workbook;

    public ExcelWorksheets Worksheets => this.Workbook.Worksheets;

    readonly Stream TargetStream;

    public ExcelUtil(ReadOnlySpan<byte> data) : this(new MemoryStream(data.ToArray())) { }

    public ExcelUtil(Stream st)
    {
        try
        {
            this.TargetStream = st;

            this.Excel = new ExcelPackage(this.TargetStream);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Excel._DisposeSafeAsync2();

            await this.TargetStream._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

