using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.ResponseCompression;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scolia.Api;
using Scolia.Api.Endpoints;
using Scolia.Api.Security;
using Scolia.Application.Abstractions;
using Scolia.Domain.Security;
using Scolia.Infrastructure;
using Scolia.Infrastructure.Backup;
using Serilog;

var app = await ScoliaHost.CreateAsync(args);
if (app is not null) await app.RunAsync();

public partial class Program;
