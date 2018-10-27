﻿using AutoMapper;
using Microsoft.Extensions.Configuration;
using Mongo.CRUD;
using Nancy.Bootstrapper;
using Nancy.Conventions;
using Nancy.ErrorHandling;
using Nancy.Scaffolding.Docs;
using Nancy.Scaffolding.Enums;
using Nancy.Scaffolding.Handlers;
using Nancy.Scaffolding.Mappers;
using Nancy.Scaffolding.Models;
using Nancy.Scaffolding.Modules;
using Nancy.Serilog.Simple;
using Nancy.Serilog.Simple.Extensions;
using Nancy.TinyIoc;
using Newtonsoft.Json;
using PackUtils;
using Serilog;
using Serilog.Builder;
using System;
using System.Globalization;
using System.Linq;

namespace Nancy.Scaffolding
{
    public class Bootstrapper : DefaultNancyBootstrapper
    {
        protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
        {
            this.EnableCors(pipelines);
            this.EnableCSRF(pipelines);
            this.SetupLogger(pipelines, container);
            this.AddRequestKey(pipelines, container);
            this.SetupMapper(container);
            this.RegisterAssemblies(container);
            Api.ApiBasicConfiguration.Pipelines?.Invoke(pipelines, container);
            SwaggerConfiguration.Register();
            MongoCRUD.RegisterDefaultConventionPack(t => true);
        }

        protected void RegisterAssemblies(TinyIoCContainer container)
        {
            if (Api.ApiBasicConfiguration.AutoRegisterAssemblies?.Any() == true)
            {
                container.AutoRegister(Api.ApiBasicConfiguration.AutoRegisterAssemblies);
            }
        }

        protected void SetupMapper(TinyIoCContainer container)
        {
            Mapper.Initialize(config =>
                Api.ApiBasicConfiguration.Mapper?.Invoke(config, container));

            var mapper = new Mapper(Mapper.Configuration);

            container.Register<IRuntimeMapper>(mapper);
            container.Register<IMapper>(mapper);
            GlobalMapper.Mapper = mapper;
        }

        protected override void ConfigureApplicationContainer(TinyIoCContainer container)
        {
            base.ConfigureApplicationContainer(container);

            container.Register<ICommunicationLogger, CommunicationLogger>();
            container.Register<IStatusCodeHandler, StatusCodeHandler>().AsSingleton();
            container.Register<IConfigurationRoot>(Api.ConfigurationRoot);

            this.RegisterJsonSettings(container);
            this.RegisterCultureSettings(container);

            Api.ApiBasicConfiguration.ApplicationContainer?.Invoke(container);
        }

        protected override void ConfigureRequestContainer(TinyIoCContainer container, NancyContext context)
        {
            base.ConfigureRequestContainer(container, context);

            this.RegisterCurrentCulture(context, container);

            Api.ApiBasicConfiguration.RequestContainer?.Invoke(context, container);
        }

        protected override void ConfigureConventions(NancyConventions conventions)
        {
            base.ConfigureConventions(conventions);

            if (Api.DocsSettings.Enabled)
            {
                SwaggerConfiguration.AddConventions(conventions);
            }
        }

        protected void RegisterJsonSettings(TinyIoCContainer container)
        {
            JsonSerializer jsonSerializer = null;
            JsonSerializerSettings jsonSerializerSettings = null;

            switch (Api.ApiSettings.JsonSerializer)
            {
                case JsonSerializerEnum.Camelcase:
                    jsonSerializer = JsonUtility.CamelCaseJsonSerializer;
                    jsonSerializerSettings = JsonUtility.CamelCaseJsonSerializerSettings;
                    break;
                case JsonSerializerEnum.Lowercase:
                    jsonSerializer = JsonUtility.LowerCaseJsonSerializer;
                    jsonSerializerSettings = JsonUtility.LowerCaseJsonSerializerSettings;
                    break;
                case JsonSerializerEnum.Snakecase:
                    jsonSerializer = JsonUtility.SnakeCaseJsonSerializer;
                    jsonSerializerSettings = JsonUtility.SnakeCaseJsonSerializerSettings;
                    break;
                default:
                    break;
            }

            BaseModule.JsonSerializerSettings = jsonSerializerSettings;
            container.Register(jsonSerializer);
            container.Register(jsonSerializerSettings);
        }

        protected void RegisterCultureSettings(TinyIoCContainer container)
        {
            var defaultLanguage = Api.ApiSettings.SupportedCultures.FirstOrDefault();
            var supportedCultures = Api.ApiSettings.SupportedCultures.Select(r => r.ToLowerInvariant().Trim());

            GlobalizationConfiguration config = new GlobalizationConfiguration(supportedCultures, defaultLanguage);

            var defaultCulture = new CultureInfo(defaultLanguage);
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

            container.Register(config);
        }

        protected void RegisterCurrentCulture(NancyContext context, TinyIoCContainer container)
        {
            var config = container.Resolve<GlobalizationConfiguration>();

            var culture = new CultureInfo(config.DefaultCulture);
            var languageHeader = context.Request.Headers
                .Where(header => header.Key == "Accept-Language");

            if (languageHeader?.Any() == true)
            {
                var language = languageHeader.First().Value.First()
                                    .Split(',').First().Trim().ToLowerInvariant();

                if (config.SupportedCultureNames.Contains(language) == true)
                {
                    culture = new CultureInfo(language);
                }
            }

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            container.Register(culture);
            context.Culture = culture;
        }

        protected void EnableCors(IPipelines pipelines)
        {
            pipelines.AfterRequest.AddItemToStartOfPipeline((context) =>
            {
                context.Response
                       .WithHeader("Access-Control-Allow-Origin", "*")
                       .WithHeader("Access-Control-Allow-Methods", "GET,HEAD,OPTIONS,POST,PUT,DELETE")
                       .WithHeader("Access-Control-Allow-Headers", "Content-Type, Accept, Authorization");
            });
        }

        protected void EnableCSRF(IPipelines pipelines)
        {
            Security.Csrf.Enable(pipelines);
        }

        protected void AddRequestKey(IPipelines pipelines, TinyIoCContainer container)
        {
            pipelines.BeforeRequest.AddItemToEndOfPipeline((context) =>
            {
                context.Items.TryGetValue("RequestKey", out object requestKey);
                container.Register(new RequestKey
                {
                    Value = requestKey?.ToString()
                });

                return null;
            });
        }

        protected void SetupLogger(IPipelines pipelines, TinyIoCContainer container)
        {
            var loggerBuilder = new LoggerBuilder();

            Log.Logger = loggerBuilder
                .UseSuggestedSetting(Api.ApiSettings.Domain, Api.ApiSettings.Application)
                .SetupSeq(Api.LogSettings.SeqOptions)
                .SetupSplunk(Api.LogSettings.SplunkOptions)
                .BuildLogger();

            var logger = container.Resolve<ICommunicationLogger>();

            logger.NancySerilogConfiguration.InformationTitle =
                Api.LogSettings.TitlePrefix + CommunicationLogger.DefaultInformationTitle;
            logger.NancySerilogConfiguration.ErrorTitle =
                Api.LogSettings.TitlePrefix + CommunicationLogger.DefaultErrorTitle;
            logger.NancySerilogConfiguration.Blacklist = Api.LogSettings.JsonBlacklist;

            pipelines.AddLogPipelines(container);
        }
    }
}
