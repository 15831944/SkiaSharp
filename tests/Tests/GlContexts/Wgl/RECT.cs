using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SkiaSharp.Tests
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct RECT
	{
		public int left;
		public int top;
		public int right;
		public int bottom;
	}
}
