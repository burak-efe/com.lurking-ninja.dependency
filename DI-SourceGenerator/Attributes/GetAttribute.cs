﻿using System;

namespace LurkingNinja.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class Get : Attribute {}
}