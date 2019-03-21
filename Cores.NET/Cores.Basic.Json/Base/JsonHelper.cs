// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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

using IPA.Cores.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class HelperJson
    {
        public static string ObjectToJson(this object obj, bool include_null = false, bool escape_html = false, int? max_depth = Json.DefaultMaxDepth, bool compact = false, bool reference_handling = false) => Json.Serialize(obj, include_null, escape_html, max_depth, compact, reference_handling);
        public static T JsonToObject<T>(this string str, bool include_null = false, int? max_depth = Json.DefaultMaxDepth) => Json.Deserialize<T>(str, include_null, max_depth);
        public static object JsonToObject(this string str, Type type, bool include_null = false, int? max_depth = Json.DefaultMaxDepth) => Json.Deserialize(str, type, include_null, max_depth);
        public static T ConvertJsonObject<T>(this object obj, bool include_null = false, int? max_depth = Json.DefaultMaxDepth, bool reference_handling = false) => Json.ConvertObject<T>(obj, include_null, max_depth, reference_handling);
        public static object ConvertJsonObject(this object obj, Type type, bool include_null = false, int? max_depth = Json.DefaultMaxDepth, bool reference_handling = false) => Json.ConvertObject(obj, type, include_null, max_depth, reference_handling);
        public static dynamic JsonToDynamic(this string str) => Json.DeserializeDynamic(str);
        public static ulong GetObjectHash(this object o) => Util.GetObjectHash(o);
    }
}
