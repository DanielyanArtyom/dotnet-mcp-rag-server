global using Lumen.Api.DTO.Responses;
global using Lumen.Domain.Entities;
global using Lumen.Domain.Enums;
global using Lumen.Infrastructure.Mapping;
global using Lumen.Infrastructure.Persistence;
global using Lumen.Infrastructure.Persistence.Entities;
global using Lumen.Infrastructure.Repositories;
global using Lumen.Infrastructure.Tenancy;
global using Lumen.IntegrationTests.Utils;
global using Lumen.Infrastructure.DependencyInjection;
global using Lumen.Infrastructure.Constants;
global using Lumen.Application.Interfaces;
global using Lumen.Eval.Api;
global using Lumen.Eval.EvalSets;
global using Lumen.Eval.Judging;
global using Lumen.Eval.Running;

global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;

global using System.Net;
global using System.Net.Http.Headers;
global using System.Net.Http.Json;
global using System.Diagnostics;
global using System.Text;
global using System.Text.Json;
global using System.Text.RegularExpressions;

global using AutoMapper;
global using Pgvector;
