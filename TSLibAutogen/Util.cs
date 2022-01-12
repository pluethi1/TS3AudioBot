using Microsoft.CodeAnalysis;
using System;

namespace TSLibAutogen;

public static class Util
{
	public const string ConversionSet =
@"
using i8  = System.SByte;
using u8  = System.Byte;
using i16 = System.Int16;
using u16 = System.UInt16;
using i32 = System.Int32;
using u32 = System.UInt32;
using i64 = System.Int64;
using u64 = System.UInt64;
using f32 = System.Single;
using f64 = System.Double;
using str = System.String;

using DateTime = System.DateTime;
using Duration = System.TimeSpan;
using DurationSeconds = System.TimeSpan;
using DurationMilliseconds = System.TimeSpan;
using DurationMillisecondsFloat = System.TimeSpan;
using SocketAddr = System.String;
using IpAddr = System.String;
using Ts3ErrorCode = TSLib.TsErrorCode;
using Ts3Permission = TSLib.TsPermission;

using IconId = System.Int32;
using ConnectionId = System.UInt32;
using EccKeyPubP256 = TSLib.Uid;
";

	public const string LicenseHeader =
@"// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.";


#pragma warning disable RS2008 // Enable analyzer release tracking
	public static readonly DiagnosticDescriptor ParseErrorDiag = new("TSDECL_1", "Parse Error", "Parse errorn in module {0}", "TSLib.Autogen", DiagnosticSeverity.Error, true);
#pragma warning restore RS2008 // Enable analyzer release tracking

	public static Exception ParseError(this GenerationContextType context, string err)
	{
		context.ReportDiagnostic(
			Diagnostic.Create(ParseErrorDiag, Location.None, err)
		);
		return new Exception(err);
	}
}
