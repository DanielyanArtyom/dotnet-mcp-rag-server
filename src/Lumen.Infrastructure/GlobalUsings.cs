global using Lumen.Application.Ingestion;
global using Lumen.Application.Interfaces;
global using Lumen.Domain;
global using Lumen.Infrastructure.DependencyInjection;
global using Lumen.Infrastructure.Persistence;
global using Lumen.Infrastructure.Persistence.Entities;
global using Lumen.Infrastructure.Tenancy;
global using Lumen.Infrastructure.Constants;
global using Lumen.Application.Exceptions;
global using Lumen.Infrastructure.Auth;
global using Lumen.Infrastructure.Chunking;
global using Lumen.Infrastructure.DocumentReading;
global using Lumen.Infrastructure.Repositories;
global using Lumen.Infrastructure.Storage;

global using Microsoft.AspNetCore.Identity;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Options;
global using Microsoft.IdentityModel.Tokens;
global using Microsoft.AspNetCore.StaticFiles;
global using Microsoft.Extensions.DependencyInjection;

global using System.Text;
global using System.Text.RegularExpressions;
global using System.IdentityModel.Tokens.Jwt;
global using System.Security.Claims;

global using AutoMapper;
