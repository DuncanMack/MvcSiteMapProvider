﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Hosting;
using Microsoft.Practices.Unity;
using MvcSiteMapProvider;
using MvcSiteMapProvider.Web;
using MvcSiteMapProvider.Web.Mvc;
using MvcSiteMapProvider.Web.Mvc.Filters;
using MvcSiteMapProvider.Web.Compilation;
using MvcSiteMapProvider.Web.UrlResolver;
using MvcSiteMapProvider.Security;
using MvcSiteMapProvider.Reflection;
using MvcSiteMapProvider.Visitor;
using MvcSiteMapProvider.Builder;
using MvcSiteMapProvider.Caching;
using MvcSiteMapProvider.Xml;
using MvcSiteMapProvider.Globalization;

namespace DI.Unity.ContainerExtensions
{
    public class MvcSiteMapProviderContainerExtension
        : UnityContainerExtension
    {
        protected override void Initialize()
        {
            bool securityTrimmingEnabled = false;
            bool enableLocalization = true;
            string absoluteFileName = HostingEnvironment.MapPath("~/Mvc.sitemap");
            TimeSpan absoluteCacheExpiration = TimeSpan.FromMinutes(5);
            string[] includeAssembliesForScan = new string[] { "$AssemblyName$" };

            var currentAssembly = this.GetType().Assembly;
            var siteMapProviderAssembly = typeof(SiteMaps).Assembly;
            var allAssemblies = new Assembly[] { currentAssembly, siteMapProviderAssembly };
            var excludeTypes = new Type[] { 
                typeof(SiteMapNodeVisibilityProviderStrategy),
                typeof(SiteMapXmlReservedAttributeNameProvider),
                typeof(SiteMapBuilderSetStrategy),
                typeof(SiteMapNodeUrlResolverStrategy),
                typeof(DynamicNodeProviderStrategy)
            };
            var multipleImplementationTypes = new Type[]  { 
                typeof(ISiteMapNodeUrlResolver), 
                typeof(ISiteMapNodeVisibilityProvider), 
                typeof(IDynamicNodeProvider) 
            };

            // Single implementations of interface with matching name (minus the "I").
            CommonConventions.RegisterDefaultConventions(
                (interfaceType, implementationType) => this.Container.RegisterType(interfaceType, implementationType, new ContainerControlledLifetimeManager()),
                new Assembly[] { siteMapProviderAssembly },
                allAssemblies,
                excludeTypes,
                string.Empty);

            // Multiple implementations of strategy based extension points
            CommonConventions.RegisterAllImplementationsOfInterface(
                (interfaceType, implementationType) => this.Container.RegisterType(interfaceType, implementationType, implementationType.Name, new ContainerControlledLifetimeManager()),
                multipleImplementationTypes,
                allAssemblies,
                excludeTypes,
                "^Composite");

            // TODO: Find a better way to inject an array constructor

            // Url Resolvers
            this.Container.RegisterType<ISiteMapNodeUrlResolverStrategy, SiteMapNodeUrlResolverStrategy>(new InjectionConstructor(
                new ResolvedArrayParameter<ISiteMapNodeUrlResolver>(this.Container.ResolveAll<ISiteMapNodeUrlResolver>().ToArray())
                ));

            // Visibility Providers
            this.Container.RegisterType<ISiteMapNodeVisibilityProviderStrategy, SiteMapNodeVisibilityProviderStrategy>(new InjectionConstructor(
                new ResolvedArrayParameter<ISiteMapNodeVisibilityProvider>(this.Container.ResolveAll<ISiteMapNodeVisibilityProvider>().ToArray()),
                new InjectionParameter<string>(string.Empty)
                ));

            // Dynamic Node Providers
            this.Container.RegisterType<IDynamicNodeProviderStrategy, DynamicNodeProviderStrategy>(new InjectionConstructor(
                new ResolvedArrayParameter<IDynamicNodeProvider>(this.Container.ResolveAll<IDynamicNodeProvider>().ToArray())
                ));


            // Pass in the global controllerBuilder reference
            this.Container.RegisterInstance<ControllerBuilder>(ControllerBuilder.Current);
            this.Container.RegisterType<IControllerBuilder, ControllerBuilderAdaptor>(new PerResolveLifetimeManager());

            this.Container.RegisterType<IBuildManager, BuildManagerAdaptor>(new PerResolveLifetimeManager());

            this.Container.RegisterType<IControllerTypeResolverFactory, ControllerTypeResolverFactory>(new InjectionConstructor(
                new List<string>(),
                new ResolvedParameter<IControllerBuilder>(),
                new ResolvedParameter<IBuildManager>()));

            // Configure Security

            // IMPORTANT: Must give arrays of object a name in Unity in order for it to resolve them.
            this.Container.RegisterType<IAclModule, AuthorizeAttributeAclModule>("authorizeAttribute");
            this.Container.RegisterType<IAclModule, XmlRolesAclModule>("xmlRoles");
            this.Container.RegisterType<IAclModule, CompositeAclModule>(new InjectionConstructor(new ResolvedArrayParameter<IAclModule>(
                new ResolvedParameter<IAclModule>("authorizeAttribute"),
                new ResolvedParameter<IAclModule>("xmlRoles"))));

#if NET35
            this.Container.RegisterType<ICacheProvider<ISiteMap>, AspNetCacheProvider<ISiteMap>>();
            this.Container.RegisterType<ICacheDependency, AspNetFileCacheDependency>(
                "cacheDependency", new InjectionConstructor(absoluteFileName));
#else
            this.Container.RegisterInstance<System.Runtime.Caching.ObjectCache>(System.Runtime.Caching.MemoryCache.Default);
            this.Container.RegisterType<ICacheProvider<ISiteMap>, RuntimeCacheProvider<ISiteMap>>();
            this.Container.RegisterType<ICacheDependency, RuntimeFileCacheDependency>(
                "cacheDependency", new InjectionConstructor(absoluteFileName));
#endif
            this.Container.RegisterType<ICacheDetails, CacheDetails>("cacheDetails",
                new InjectionConstructor(absoluteCacheExpiration, TimeSpan.MinValue, new ResolvedParameter<ICacheDependency>("cacheDependency")));

            // Configure the visitors
            this.Container.RegisterType<ISiteMapNodeVisitor, UrlResolvingSiteMapNodeVisitor>();

            // Register the sitemap builders
            this.Container.RegisterType<IXmlSource, FileXmlSource>("file1XmlSource", new InjectionConstructor(absoluteFileName));
            this.Container.RegisterType<ISiteMapXmlReservedAttributeNameProvider, SiteMapXmlReservedAttributeNameProvider>("nameProvider", new InjectionConstructor(new List<string>()));


            // IMPORTANT: Must give arrays of object a name in Unity in order for it to resolve them.
            this.Container.RegisterType<ISiteMapBuilder, XmlSiteMapBuilder>("xmlSiteMapBuilder", new InjectionConstructor(new ResolvedParameter<IXmlSource>("file1XmlSource"), new ResolvedParameter<ISiteMapXmlReservedAttributeNameProvider>("nameProvider"), typeof(INodeKeyGenerator), typeof(IDynamicNodeBuilder), typeof(ISiteMapNodeFactory), typeof(ISiteMapXmlNameProvider)));
            this.Container.RegisterType<ISiteMapBuilder, ReflectionSiteMapBuilder>("reflectionSiteMapBuilder", new InjectionConstructor(includeAssembliesForScan, new List<string>(), new ResolvedParameter<ISiteMapXmlReservedAttributeNameProvider>("nameProvider"), typeof(INodeKeyGenerator), typeof(IDynamicNodeBuilder), typeof(ISiteMapNodeFactory), typeof(ISiteMapCacheKeyGenerator)));
            this.Container.RegisterType<ISiteMapBuilder, VisitingSiteMapBuilder>("visitingSiteMapBuilder");
            this.Container.RegisterType<ISiteMapBuilder, CompositeSiteMapBuilder>(new InjectionConstructor(new ResolvedArrayParameter<ISiteMapBuilder>(
                new ResolvedParameter<ISiteMapBuilder>("xmlSiteMapBuilder"),
                new ResolvedParameter<ISiteMapBuilder>("reflectionSiteMapBuilder"),
                new ResolvedParameter<ISiteMapBuilder>("visitingSiteMapBuilder"))));

            // Configure the builder sets

            this.Container.RegisterType<ISiteMapBuilderSet, SiteMapBuilderSet>("builderSet1",
                new InjectionConstructor(
                    "default",
                    securityTrimmingEnabled,
                    enableLocalization,
                    new ResolvedParameter<ISiteMapBuilder>(),
                    new ResolvedParameter<ICacheDetails>("cacheDetails")));

            this.Container.RegisterType<ISiteMapBuilderSetStrategy, SiteMapBuilderSetStrategy>(new InjectionConstructor(new ResolvedArrayParameter<ISiteMapBuilderSet>(new ResolvedParameter<ISiteMapBuilderSet>("builderSet1"))));
        }
    }
}