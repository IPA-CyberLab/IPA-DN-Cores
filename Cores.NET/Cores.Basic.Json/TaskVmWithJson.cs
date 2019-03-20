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
using System.Threading.Tasks;
using System.Reflection;
using System.Threading;

namespace IPA.Cores.Basic
{
    static partial class TaskUtil
    {
        public static object ConvertTask(object src_task_object, Type old_task_type, Type new_task_type)
        {
            Type src_task_def = typeof(Task<>).MakeGenericType(old_task_type);

            var cont_with_methods = src_task_def.GetMethods();
            MethodInfo cont_with = null;
            int num = 0;
            foreach (var m in cont_with_methods)
            {
                if (m.Name == "ContinueWith" && m.ContainsGenericParameters)
                {
                    var pinfos = m.GetParameters();
                    if (pinfos.Length == 1)
                    {
                        var pinfo = pinfos[0];
                        var ptype = pinfo.ParameterType;
                        var generic_args = ptype.GenericTypeArguments;
                        if (generic_args.Length == 2)
                        {
                            if (generic_args[0].IsGenericType)
                            {
                                if (generic_args[1].IsGenericParameter)
                                {
                                    if (generic_args[0].BaseType == typeof(Task))
                                    {
                                        cont_with = m;
                                        num++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (num != 1) throw new ApplicationException("ConvertTask: num != 1");

            object ret = null;

            var cont_with_generic = cont_with.MakeGenericMethod(new_task_type);

            var convert_task_proc_method = typeof(TaskUtil).GetMethod(nameof(convert_task_proc), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(new_task_type);

            var func_type = typeof(Func<,>).MakeGenericType(typeof(Task<>).MakeGenericType(old_task_type), new_task_type);

            Delegate delegate_instance = convert_task_proc_method.CreateDelegate(func_type);

            ret = cont_with_generic.Invoke(src_task_object, new object[] { delegate_instance });

            return ret;
        }

        static TNewResult convert_task_proc<TNewResult>(object t)
        {
            Type old_task_type = t.GetType();
            object result_old = old_task_type.GetProperty("Result").GetValue(t);
            TNewResult result_new = Json.ConvertObject<TNewResult>(result_old);
            return result_new;
        }
    }
}

