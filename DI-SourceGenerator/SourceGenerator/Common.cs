﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LurkingNinja.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LurkingNinja.SourceGenerator
{
    internal static class Common
    {
        private const string FILENAME_POSTFIX = "_codegen.cs";
        
        internal const string GET_ATTRIBUTE = nameof(Get);
        internal const string GET_BY_NAME_ATTRIBUTE = nameof(GetByName);
        internal const string GET_BY_TAG_ATTRIBUTE = nameof(GetByTag);
        internal const string GET_IN_ASSETS_ATTRIBUTE = nameof(GetInAssets);
        internal const string GET_IN_CHILDREN_ATTRIBUTE = nameof(GetInChildren);
        internal const string GET_IN_PARENT_ATTRIBUTE = nameof(GetInParent);
        internal const string IGNORE_SELF_ATTRIBUTE = nameof(IgnoreSelf);
        internal const string SKIP_NULL_CHECK_ATTRIBUTE = nameof(SkipNullCheck);
        internal const string INCLUDE_INACTIVE_ATTRIBUTE = nameof(IncludeInactive);
        internal const string INJECT_IN_PLAY_ATTRIBUTE = nameof(InjectInPlay);

        internal static readonly string[] ValidAttributes =
        {
            GET_ATTRIBUTE, GET_BY_NAME_ATTRIBUTE, GET_BY_TAG_ATTRIBUTE, GET_IN_ASSETS_ATTRIBUTE,
            GET_IN_CHILDREN_ATTRIBUTE, GET_IN_PARENT_ATTRIBUTE
        };

        /*
         * {0} name space if exists
         * {1} closing bracket for namespace if needed
         * {2} class definition
         * {3} using directives
         */
        private const string NS_TEMPLATE = @"{3}
{0}
    {2}
{1}";

        private static string NamespaceTemplateResolve(string usingDirectives, string nameSpace, string source)
        {
            var ns = GetNamespaceTemplate(nameSpace);
            return string.Format(NS_TEMPLATE,
                /*{0}*/ns.Item1,
                /*{1}*/ns.Item2,
                /*{2}*/source,
                /*{3}*/usingDirectives);
        }

        internal static string GetClassNameOf(SyntaxNode node) =>
            GetClassOf(node).Identifier.ValueText;

        internal static string GetIfSealed(ClassDeclarationSyntax cds) =>
            IsSealed(cds) ? "sealed " : string.Empty;

        private static bool IsSealed(MemberDeclarationSyntax cds) =>
            cds.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.SealedKeyword));

        private static bool IsPublic(ClassDeclarationSyntax cds) =>
            cds.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword));

        private static bool IsPrivate(ClassDeclarationSyntax cds) =>
            cds.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword));

        internal static bool IsCollection(SyntaxNode syntaxNode, string whatCollection  = "List")
        {
            switch (syntaxNode)
            {
                case FieldDeclarationSyntax fds:
                    return fds.Declaration.Type.ToString().Contains(whatCollection);
                case PropertyDeclarationSyntax pds:
                    return pds.Type.ToString().Contains(whatCollection);
                default:
                    return false;
            }
        }
        
        internal static bool IsArray(SyntaxNode syntaxNode) 
        {
            switch (syntaxNode)
            {
                case FieldDeclarationSyntax fds:
                    return fds.Declaration.Type.IsKind(SyntaxKind.ArrayType);
                case PropertyDeclarationSyntax pds:
                    return pds.Type.IsKind(SyntaxKind.ArrayType);
                default:
                    return false;
            }
        }

        internal static string GetAccessOfClass(ClassDeclarationSyntax cds) =>
            IsPublic(cds) ? "public" : IsPrivate(cds) ? "private" : "internal";

        internal static ClassDeclarationSyntax GetClassOf(SyntaxNode node)
        {
            foreach (var syntaxNode in node.Ancestors())
            {
                if (syntaxNode is ClassDeclarationSyntax cds) return cds;
            }

            return null;
        }
        
        internal static bool IsPartial(ClassDeclarationSyntax cds) =>
            cds.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));

        internal static SyntaxNode HasAnyAttribute(GeneratorSyntaxContext ctx, CancellationToken token) =>
            ValidAttributes.Any(attribute => HasAttribute(ctx.Node, attribute))
                ? ctx.Node
                : null;

        private static SyntaxList<AttributeListSyntax> GetAttributeList(SyntaxNode syntaxNode)
        {
            switch (syntaxNode)
            {
                case FieldDeclarationSyntax fds:
                    return fds.AttributeLists;
                case PropertyDeclarationSyntax pds:
                    return pds.AttributeLists;
                default:
                    return new SyntaxList<AttributeListSyntax>();
            }
        }
        
        internal static bool HasAttribute(SyntaxNode syntaxNode, string attributeName) =>
            GetAttributeList(syntaxNode)
                .SelectMany(nodeAttribute => nodeAttribute.Attributes)
                .Any(attribute => attribute.Name.ToString().Trim().ToLower()
                    .Equals(attributeName.Trim().ToLower()));

        internal static bool TryGetFieldOrPropertyData(SyntaxNode syntaxNode, out string type, out string fieldName)
        {
            switch (syntaxNode)
            {
                case PropertyDeclarationSyntax pds:
                    type = pds.Type.ToString();
                    fieldName = pds.Identifier.ValueText;
                    return true;
                case FieldDeclarationSyntax fds:
                {
                    type = fds.Declaration.Type.ToString();
                    fieldName = fds.Declaration.Variables[0].Identifier.ValueText;
                    return true;
                }
            }
            
            type = null;
            fieldName = null;
            return false;
        }

        internal static string GetAttributeParam(SyntaxNode syntaxNode, string attribute, string parameter, Compilation comp)
        {
            if (syntaxNode?.Parent == null) return null;
            
            var semanticModel = comp?.GetSemanticModel(syntaxNode.SyntaxTree, true);
            
            ISymbol symbol;
            switch (syntaxNode)
            {
                case FieldDeclarationSyntax fds:
                    symbol = semanticModel.GetDeclaredSymbol(fds.Declaration.Variables.First());
                    break;
                case PropertyDeclarationSyntax pds:
                    symbol = semanticModel.GetDeclaredSymbol(pds);
                    break;
                default:
                    return null;
            }
            if (symbol is null) return null;
            
            foreach (var attributeData in symbol.GetAttributes())
            {
                if (attributeData.AttributeClass is null
                    || !attributeData.AttributeClass.Name.Equals(attribute)) continue;
                if (!attributeData.ConstructorArguments.IsDefaultOrEmpty)
                    return attributeData.ConstructorArguments[0].Value?.ToString();
                if (!attributeData.NamedArguments.IsDefaultOrEmpty) return null;

                foreach (var namedArgument in attributeData.NamedArguments)
                {
                    if (namedArgument.Key.Trim().Equals(parameter.Trim())) return namedArgument.Value.ToString();
                }
            }

            return null;
        }
        
        private static void AddSource(SourceProductionContext context, string fileName, string source) =>
            context.AddSource($"{fileName}{FILENAME_POSTFIX}", source);
        
        internal static void AddSourceNs(SourceProductionContext ctx, string filename,
                string usingDirectives, ClassDeclarationSyntax cds, string source, bool log = false)
        {
            source = NamespaceTemplateResolve(usingDirectives, GetNamespace(cds), source);
            AddSource(ctx, filename, source);
            
            if (!log) return;
            //ctx.ReportDiagnostic(Diagnostic.Create(logMessage, cds.GetLocation(), source));
            Log(source);
        }

        internal static string Toggle(bool isOn, string ifOn) => isOn ? ifOn : string.Empty;
        internal static string Toggle(bool isOn, string ifOn, string ifOff) => isOn ? ifOn : ifOff;

        private static string GetNamespace(SyntaxNode node)
        {
            var nameSpace = string.Empty;
            var potentialNamespaceParent = node.Parent;

            while (potentialNamespaceParent != null
                   && !(potentialNamespaceParent is NamespaceDeclarationSyntax))
                potentialNamespaceParent = potentialNamespaceParent.Parent;

            if (!(potentialNamespaceParent is NamespaceDeclarationSyntax namespaceParent)) return nameSpace;
            
            nameSpace = namespaceParent.Name.ToString();

            while (true)
            {
                if (!(namespaceParent.Parent is NamespaceDeclarationSyntax parent)) break;

                nameSpace = $"{namespaceParent.Name}.{nameSpace}";
                namespaceParent = parent;
            }

            return string.IsNullOrEmpty(nameSpace)
                ? string.Empty
                : nameSpace;
        }

        private static (string, string) GetNamespaceTemplate(string potentialNamespace)
        {
            var isNullOrEmpty = string.IsNullOrEmpty(potentialNamespace); 
            return (
                isNullOrEmpty
                    ? string.Empty
                    : $"namespace {potentialNamespace}\n{{",
                isNullOrEmpty
                    ? string.Empty
                    : "}");
        }

        internal static string GetUsingDirectives(ClassDeclarationSyntax cds)
        {
            var usingDirectives = new HashSet<string>();
            foreach (var child in cds.SyntaxTree.GetRoot().ChildNodes())
                if (child.IsKind(SyntaxKind.UsingDirective))
                    usingDirectives.Add(child.ToString());
            usingDirectives.Add("using System;");
            usingDirectives.Add("using UnityEngine;");

            return string.Join("\n", usingDirectives);
        }

        internal static void Log(string text) =>
            File.AppendAllText("D:\\DI-SourceGenerator.log", $"{text}\n");
    }
}