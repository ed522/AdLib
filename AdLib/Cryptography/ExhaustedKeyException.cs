using System;

namespace AdLib.Cryptography;

public class ExhaustedKeyException(string message) : Exception(message);
