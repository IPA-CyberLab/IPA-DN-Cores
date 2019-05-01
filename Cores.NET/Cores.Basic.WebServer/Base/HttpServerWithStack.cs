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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class InternalOverrideClassTypeBuilder
    {
        readonly TypeBuilder TypeBuilder;

        Type BuiltType = null;

        public InternalOverrideClassTypeBuilder(Type originalType, string typeNameSuffix = "Ex")
        {
            Assembly originalAsm = originalType.Assembly;
            string friendAssemblyName = originalAsm.GetCustomAttributes<InternalsVisibleToAttribute>().Where(x => x.AllInternalsVisible).First().AssemblyName;

            AssemblyName asmName = new AssemblyName(friendAssemblyName);
            AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = asmBuilder.DefineDynamicModule(Guid.NewGuid().ToString());

            TypeBuilder = moduleBuilder.DefineType(originalType.Name + typeNameSuffix, TypeAttributes.Public | TypeAttributes.Class, originalType);

            ConstructorBuilder emptyConstructor = TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, null);
            ILGenerator il = emptyConstructor.GetILGenerator();
            il.Emit(OpCodes.Ret);
        }

        public void AddOverloadMethod(string name, Delegate m, Type retType, params Type[] argsType)
            => AddOverloadMethod(name, m.GetMethodInfo(), retType, argsType);

        public void AddOverloadMethod(string name, MethodInfo methodInfoToCall, Type retType, params Type[] argsType)
        {
            if (BuiltType != null) throw new ApplicationException("BuildType() has been already called.");

            if (argsType == null) argsType = new Type[0];

            MethodBuilder newMethod = this.TypeBuilder.DefineMethod("BindAsync",
                MethodAttributes.Virtual | MethodAttributes.Public,
                retType,
                argsType);


            var il = newMethod.GetILGenerator();

            if (methodInfoToCall.IsStatic)
            {
                for (int i = 0; i < argsType.Length + 1; i++)
                    il.Emit(OpCodes.Ldarg, i);
            }
            else
            {
                il.Emit(OpCodes.Ldarg, 0);
                for (int i = 0; i < argsType.Length + 1; i++)
                    il.Emit(OpCodes.Ldarg, i);
            }

            il.Emit(OpCodes.Call, methodInfoToCall);
            il.Emit(OpCodes.Ret);
        }

        public Type BuildType()
        {
            if (BuiltType == null)
                BuiltType = this.TypeBuilder.CreateType();

            return BuiltType;
        }

        public object NewUninitializedbject()
            => Util.NewWithoutConstructor(this.BuildType());
    }

    static class HttpServerWithStackUtil
    {
        public static Task Test(ListenOptions targetObject, object param)
        {
            Con.WriteLine(targetObject.ToString());

            return Task.CompletedTask;
        }

        public static ListenOptions NewListenOptions(IPEndPoint endPoint)
        {
            InternalOverrideClassTypeBuilder builder = new InternalOverrideClassTypeBuilder(typeof(ListenOptions));

            builder.AddOverloadMethod("BindAsync",
                typeof(HttpServerWithStackUtil).GetMethod("Test"),
                typeof(Task),
                typeof(ListenOptions).Assembly.GetType("Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBindContext"));

            //builder.AddOverloadMethod("BindAsync",
            //    new Func<ListenOptions, object, Task>(
            //        (targetObject, param) =>
            //        {
            //            Con.WriteLine(targetObject.ToString());
            //            return Task.CompletedTask;
            //        }),
            //    typeof(Task),
            //    typeof(ListenOptions).Assembly.GetType("Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBindContext"));

            ListenOptions ret = (ListenOptions)builder.NewUninitializedbject();

            ret.PrivateSet<ListenOptions>("Type", ListenType.IPEndPoint);
            ret.PrivateSet<ListenOptions>("IPEndPoint", endPoint);
            ret.PrivateSet<ListenOptions>("NoDelay", true);
            ret.PrivateSet<ListenOptions>("Protocols", HttpProtocols.Http1AndHttp2);
            ret.PrivateSet<ListenOptions>("ConnectionAdapters", new List<IConnectionAdapter>());

            object a = ret.PrivateInvoke("BindAsync", null);

            return ret;
        }
    }
}
