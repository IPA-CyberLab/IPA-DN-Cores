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
using Newtonsoft.Json;
using Castle.DynamicProxy;
using System.Reflection;
using System.Reflection.Emit;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class ProxyInterceptor : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            invocation.Debug();
        }
    }

    static class HttpServerWithStackUtil
    {
        static Task Test(object param)
        {
            return Task.CompletedTask;
        }

        public static ListenOptions NewListenOptions(IPEndPoint endPoint)
        {
            Type typeofOriginal = typeof(ListenOptions);

            //string name = "DefineMethodOverrideExample";
            AssemblyName asmName = new AssemblyName("Microsoft.AspNetCore.Server.Kestrel.Tests");
            asmName.SetPublicKey(
                "0024000004800000940000000602000000240000525341310004000001000100f33a29044fa9d740c9b3213a93e57c84b472c84e0b8a0e1ae48e67a9f8f6de9d5f7f3d52ac23e48ac51801f1dc950abe901da34d2a9e3baadb141a17c77ef3c565dd5ee5054b91cf63bb3c6ab83f72ab3aafe93d0fc3c2348b764fafb0b1c0733de51459aeab46580384bf9d74c4e28164b7cde247f891ba07891c9d872ad2bb"
                .GetHexBytes());
            AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            ModuleBuilder modBuilder = asmBuilder.DefineDynamicModule("MainModule");

            TypeBuilder typeBuilder = modBuilder.DefineType(typeofOriginal.Name + "_Ex", TypeAttributes.Public | TypeAttributes.Class, typeofOriginal);

            //TypeBuilder typeBuilder = typeofOriginal.Assembly.GetModule().bui

            ConstructorBuilder emptyConstructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, null);
            ILGenerator il = emptyConstructor.GetILGenerator();
            il.Emit(OpCodes.Ret);

            MethodInfo methodToOverride = typeofOriginal.GetMethod("BindAsync", BindingFlags.NonPublic | BindingFlags.Instance);

            MethodInfo methodToCall = typeof(HttpServerWithStackUtil).GetMethod("Test", BindingFlags.NonPublic | BindingFlags.Static);

            Type arg0Type = typeofOriginal.Assembly.GetType("Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBindContext");

            MethodBuilder newMethod = typeBuilder.DefineMethod("BindAsync",
                MethodAttributes.Virtual | MethodAttributes.Public,
                typeof(Task),
                new Type[] { arg0Type });

            il = newMethod.GetILGenerator();
            //il.Emit(OpCodes.Call, methodToCall);
            il.Emit(OpCodes.Ret);


            Type newType = typeBuilder.CreateType();


            ListenOptions ob = (ListenOptions)Util.NewWithoutConstructor(newType);

            //ProxyGenerator g = new ProxyGenerator();
            //ProxyInterceptor ic = new ProxyInterceptor();

            ////ProxyGenerationOptions opt = new ProxyGenerationOptions();

            //var x = g.CreateClassProxy(newType, ic);

            //Con.WriteLine(x is ListenOptions);

            //ListenOptions optt = (ListenOptions)x;

            object reta = ob.PrivateInvoke("BindAsync", null);

            return null;




            ListenOptions ret = Util.NewWithoutConstructor<ListenOptions>();

            ret.PrivateSet("Type", ListenType.IPEndPoint);
            ret.PrivateSet("IPEndPoint", endPoint);
            ret.PrivateSet("NoDelay", true);
            ret.PrivateSet("Protocols", HttpProtocols.Http1AndHttp2);
            ret.PrivateSet("ConnectionAdapters", new List<IConnectionAdapter>());



            return ret;
        }
    }
}
