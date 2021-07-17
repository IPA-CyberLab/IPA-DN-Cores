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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using Microsoft.AspNetCore.Mvc.Razor;

namespace IPA.Cores.Web
{
    public class AspPageContext
    {
        // タイトルのベース文字列 (共通的な Web サイト名など)
        public string SiteName { get; set; } = "WebSite";

        // タイトル文字列
        string? TitleInternal;

        // タイトル文字列の取得と設定
        public virtual string Title
        {
            get => TitleInternal._NonNullTrim();
            set => TitleInternal = value;
        }

        // フルタイトル文字列の取得
        public string FullTitle => GenerateFullTitleImpl();

        // フルタイトル文字列の取得 (派生クラスで挙動変更可能)
        protected virtual string GenerateFullTitleImpl()
        {
            if (this.Title._IsFilled())
            {
                return $"{this.Title._NonNullTrim()} - {SiteName._NonNullTrim()}";
            }
            else
            {
                return SiteName._NonNullTrim();
            }
        }

        // 現在の言語と文字列テーブル
        public StrTableLanguageList LanguageList { get; private set; } = StrTableLanguageList.DefaultEmptyStrTableLanguageList;
        public StrTableLanguage Language { get; private set; } = StrTableLanguageList.DefaultEmptyStrTableLanguageList.FindDefaultLanguage();
        public StrTable StrTable => this.Language.Table;

        public void SetLanguageList(StrTableLanguageList list)
        {
            this.LanguageList = list;
        }

        public void SetLanguageByHttpString(string str)
        {
            this.Language = this.LanguageList.FindLanguageByHttpAcceptLanguage(str);
        }

        // 現在の Language List を元に文字列テーブルファイルを吐き出す 
        public void DumpStrTableJson(FilePath destFileName)
        {
            var objRoot = Json.NewJsonObject();

            foreach (var lang in this.LanguageList.GetLaugnageList().OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer))
            {
                var obj2 = Json.NewJsonObject();

                var table = lang.Table.ToList();

                foreach (var kv in table)
                {
                    obj2.Add(kv.Key.ToUpper(), new Newtonsoft.Json.Linq.JValue(kv.Value));
                }

                objRoot.Add(lang.Key.ToLower(), obj2);
            }

            string jsonBody = objRoot._ObjectToJson(compact: true);

            StringWriter w = new StringWriter();
            w.WriteLine("// String table");
            w.WriteLine("// Automatically generated file");
            w.WriteLine();
            w.WriteLine("// --- BEGIN OF STRTABLE ---");
            w.WriteLine();
            w.WriteLine("var g_ipa_dn_cores_strtable =");
            w.WriteLine(jsonBody);
            w.WriteLine();
            w.WriteLine("// --- END OF STRTABLE ---");
            w.WriteLine();

            destFileName.WriteStringToFile(w.ToString()._NormalizeCrlf(CrlfStyle.Lf), FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged, writeBom: true);
        }
    }
}


