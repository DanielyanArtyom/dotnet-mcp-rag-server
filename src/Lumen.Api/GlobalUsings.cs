global using Lumen.Api.DTO.Responses;
global using Lumen.Application.Auth;
global using Lumen.Domain.Entities;
global using Lumen.Domain.Enums;
global using Lumen.Infrastructure.Constants;
global using Lumen.Infrastructure.DependencyInjection;
global using Lumen.Infrastructure.Persistence;
global using Lumen.Infrastructure.Tenancy;
global using Lumen.Api.DTO.Requests;
global using Lumen.Api.Middleware;
global using Lumen.Api.ErrorHandling;
global using Lumen.Api.Mapping;
global using Lumen.Infrastructure.Mapping;
global using Lumen.Application.Exceptions;
global using Lumen.Application.Documents.Commands;
global using Lumen.Application.Documents.Queries;
global using Lumen.Api.Extensions;

global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Diagnostics;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.AspNetCore.Authentication.JwtBearer;
global using Microsoft.IdentityModel.Tokens;

global using System.Security.Claims;
global using System.Text;

global using AutoMapper;
global using MediatR;
global using NLog.Web;
