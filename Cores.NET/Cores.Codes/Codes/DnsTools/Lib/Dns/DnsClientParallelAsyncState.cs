#region Copyright and License
// Copyright 2010..2014 Alexander Reinert
// 
// This file is part of the ARSoft.Tools.Net - C# DNS client/server and SPF Library (http://arsofttoolsnet.codeplex.com/)
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System;
using System.Collections.Generic;
using System.Threading;

namespace ARSoft.Tools.Net.Dns
{
	internal class DnsClientParallelAsyncState<TMessage> : IAsyncResult
		where TMessage : DnsMessageBase
	{
		internal int ResponsesToReceive;
		internal List<TMessage> Responses;

		internal AsyncCallback UserCallback;
		public object AsyncState { get; internal set; }
		public bool IsCompleted { get; private set; }

		public bool CompletedSynchronously
		{
			get { return false; }
		}

		private ManualResetEvent _waitHandle;

		public WaitHandle AsyncWaitHandle
		{
			get { return _waitHandle ?? (_waitHandle = new ManualResetEvent(IsCompleted)); }
		}

		internal void SetCompleted()
		{
			IsCompleted = true;

			if (_waitHandle != null)
				_waitHandle.Set();

			if (UserCallback != null)
				UserCallback(this);
		}
	}
}