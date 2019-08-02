﻿// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public class AmbiguousMasterData<T> : AmbiguousSearch<T> where T: class
    {
        public AmbiguousMasterData(string body, Func<string, T> parser, bool allowWildcard = false) : base(allowWildcard)
        {
            string[] lines = body._GetLines();

            foreach (string line in lines)
            {
                string line2 = line._StripCommentFromLine()._NonNullTrim();

                if (line2._IsFilled())
                {
                    if (line2._GetKeyAndValue(out string key, out string value))
                    {
                        try
                        {
                            T t = parser(value);

                            if (t != default)
                            {
                                this.Add(key, t);
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }
            }
        }
    }

    public static class MasterData
    {
        public static AmbiguousMasterData<Tuple<string, string>> MimeToFasIcon => MimeToFasIconSingleton;
        static readonly Singleton<AmbiguousMasterData<Tuple<string, string>>> MimeToFasIconSingleton =
            new Singleton<AmbiguousMasterData<Tuple<string, string>>>(() => new AmbiguousMasterData<Tuple<string, string>>(
                allowWildcard: true,
                body: CoresRes["MasterData/MimeToFasIcon/MimeToFasIconList.txt"].String,
                parser: line =>
                {
                    string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ", "　", "\t");
                    if (tokens.Length == 2)
                    {
                        return new Tuple<string, string>(tokens[0], tokens[1]);
                    }
                    return null;
                }));

        public static MimeList ExtensionToMime => ExtensionToMimeSingleton;
        static readonly Singleton<MimeList> ExtensionToMimeSingleton =
            new Singleton<MimeList>(() => new MimeList(
                body: CoresRes["MasterData/MimeLookup/MimeList.txt"].String
                ));

        public static Tuple<string, string> GetFasIconFromExtension(string extensionOrMimeType)
        {
            // Mime type search
            var ret = MimeToFasIcon.SearchTopWithCache(extensionOrMimeType);
            if (ret != null) return ret;

            // Extension search
            ret = MimeToFasIcon.SearchTopWithCache(ExtensionToMime.Get(extensionOrMimeType));
            return ret;
        }

        public class MimeList
        {
            Dictionary<string, string> ExtToMimeDictionary = new Dictionary<string, string>(StrComparer.IgnoreCaseComparer);

            public MimeList(string body)
            {
                string[] lines = body._GetLines();

                HashSetDictionary<string, string> extToMime = new HashSetDictionary<string, string>(StrComparer.IgnoreCaseComparer, StrComparer.IgnoreCaseComparer);
                HashSetDictionary<string, string> mimeToExt = new HashSetDictionary<string, string>(StrComparer.IgnoreCaseComparer, StrComparer.IgnoreCaseComparer);

                foreach (string line in lines)
                {
                    string line2 = line._StripCommentFromLine()._NonNullTrim();

                    if (line2._IsFilled())
                    {
                        if (line2._GetKeyAndValue(out string key, out string value))
                        {
                            key = key.ToLower();
                            value = value.ToLower();

                            if (key.StartsWith(".")) key = key.Substring(1);

                            if (key._IsFilled() && value._IsFilled())
                            {
                                extToMime.Add(key, value);
                                mimeToExt.Add(value, key);
                            }
                        }
                    }
                }

                foreach (var extInfo in extToMime)
                {
                    string mime = extInfo.Value.OrderBy(x => mimeToExt[x].Count).FirstOrDefault();
                    if (mime._IsFilled())
                    {
                        this.ExtToMimeDictionary.Add(extInfo.Key, mime);
                    }
                }
            }

            public string Get(string ext, string defaultMimeType = Consts.MimeTypes.OctetStream)
            {
                if (ext.StartsWith("."))
                {
                    ext = ext.Substring(1);
                }

                if (ExtToMimeDictionary.TryGetValue(ext, out string ret))
                {
                    return ret;
                }
                else
                {
                    return defaultMimeType;
                }
            }
        }
    }
}
