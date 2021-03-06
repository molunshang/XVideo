﻿using System;
using System.Linq;

namespace AsyncNet
{
    public static class Utils
    {
        public static bool IsNotNullOrEmpty(this string str)
        {
            return !string.IsNullOrEmpty(str);
        }

        public static bool IsNotNullOrWhiteSpace(this string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }

        public static byte ToByte(this char c)
        {
            return (byte)(c - 48);
        }
    }
}