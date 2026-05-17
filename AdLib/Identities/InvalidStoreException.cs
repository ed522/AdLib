using System;

namespace AdLib.Identities;

public class InvalidStoreException(string msg) : Exception(msg);
