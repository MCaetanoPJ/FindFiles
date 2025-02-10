using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApiAnalysis
{
    class Program
    {
        static void Main()
        {
            // Caminho da sua solution ou projeto
            string projectPath = @"C:\Users\mcpes\source\repos\Portal\";
            // Caminho para salvar o relatório final
            string outputFilePath = Path.Combine(projectPath, "ResultadoLocalizacao.txt");

            // Métodos da classe que fazem chamadas à API
            string[] targetApiMethods = { "GET_JWT", "POST_JWT", "PUT_JWT", "PATCH_JWT", "DELETE_JWT", "ObtenhaBearerToken" };

            // Obter todos os arquivos .cs do projeto
            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories);

            // Coleção para armazenar informações das invocações de API encontradas
            ConcurrentBag<ApiInvocationInfo> apiInvocations = new ConcurrentBag<ApiInvocationInfo>();

            // Dicionário para mapear: chave = nome do método chamado; valor = conjunto de métodos (MethodInfo) que o chamam.
            // Isso evita varrer repetidamente todos os arquivos para achar os "callers".
            ConcurrentDictionary<string, ConcurrentDictionary<MethodInfo, byte>> callerMapping =
                new ConcurrentDictionary<string, ConcurrentDictionary<MethodInfo, byte>>();

            // Processa os arquivos .cs em paralelo para otimizar a performance
            Parallel.ForEach(csFiles, file =>
            {
                string code = File.ReadAllText(file);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                // Itera por todas as invocações no arquivo
                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    // Verifica se a invocação possui uma expressão do tipo MemberAccess (ex.: objeto.Metodo)
                    var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
                    if (memberAccess == null)
                        continue;

                    string invokedMethodName = memberAccess.Name.Identifier.Text;

                    // Obtém o método imediato que contém a invocação
                    var immediateMethodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    if (immediateMethodDecl == null)
                        continue;

                    MethodInfo immediateMethod = new MethodInfo
                    {
                        MethodName = immediateMethodDecl.Identifier.Text,
                        FilePath = file
                    };

                    // Adiciona essa relação no dicionário de callers
                    var callersForMethod = callerMapping.GetOrAdd(invokedMethodName, _ => new ConcurrentDictionary<MethodInfo, byte>());
                    callersForMethod.TryAdd(immediateMethod, 0);

                    // Se o método invocado for um dos métodos de API, registra a invocação
                    if (targetApiMethods.Contains(invokedMethodName))
                    {
                        // Tenta extrair o valor do primeiro parâmetro (endpoint)
                        string endpointArg = invocation.ArgumentList.Arguments.Count > 0
                            ? GetArgumentValue(invocation.ArgumentList.Arguments[0], immediateMethodDecl)
                            : "Sem parâmetro";

                        ApiInvocationInfo info = new ApiInvocationInfo
                        {
                            ApiFile = file,
                            ApiMethodCalled = invokedMethodName,
                            EndpointArgument = endpointArg,
                            ImmediateMethod = immediateMethod
                        };

                        apiInvocations.Add(info);
                    }
                }
            });

            // Para cada invocação de API, determina recursivamente o método "pai" (top-level)
            foreach (var apiInvocation in apiInvocations)
            {
                apiInvocation.TopLevelMethod = GetTopLevelMethod(apiInvocation.ImmediateMethod, callerMapping);
                // Procura no projeto o arquivo ASPX que contenha um controle cujo OnClick seja igual ao método top-level
                apiInvocation.AspxButtonInfo = FindAspxButtonInfo(apiInvocation.TopLevelMethod.MethodName, projectPath);
            }

            // Monta o relatório final usando StringBuilder
            StringBuilder sb = new StringBuilder();
            foreach (var info in apiInvocations)
            {
                sb.AppendLine("--------------------------------------------------------");
                sb.AppendLine($"API chamada no arquivo: {info.ApiFile}");
                sb.AppendLine($"Método da API: {info.ApiMethodCalled}");
                sb.AppendLine($"Endpoint: {info.EndpointArgument}");
                sb.AppendLine($"Método imediato: {info.ImmediateMethod.MethodName} (Arquivo: {info.ImmediateMethod.FilePath})");
                sb.AppendLine($"Método pai (top-level): {info.TopLevelMethod.MethodName} (Arquivo: {info.TopLevelMethod.FilePath})");
                if (info.AspxButtonInfo != null)
                {
                    sb.AppendLine($"Arquivo ASPX: {info.AspxButtonInfo.FilePath}");
                    sb.AppendLine($"Texto do botão: {info.AspxButtonInfo.ButtonText}");
                }
                else
                {
                    sb.AppendLine("Arquivo ASPX não encontrado para este evento.");
                }
            }

            // Salva o relatório em um arquivo de texto
            File.WriteAllText(outputFilePath, sb.ToString());
            Console.WriteLine($"Relatório salvo em: {outputFilePath}");
        }

        /// <summary>
        /// Função recursiva que, usando o índice de callers, retorna o método top-level (aquele que não é chamado por nenhum outro).
        /// Utiliza um conjunto "visited" para evitar ciclos.
        /// </summary>
        static MethodInfo GetTopLevelMethod(MethodInfo currentMethod,
            ConcurrentDictionary<string, ConcurrentDictionary<MethodInfo, byte>> callerMapping,
            HashSet<MethodInfo> visited = null)
        {
            if (visited == null)
                visited = new HashSet<MethodInfo>();

            if (!visited.Add(currentMethod))
                return currentMethod; // Evita loop

            if (!callerMapping.TryGetValue(currentMethod.MethodName, out var callers) || callers.IsEmpty)
                return currentMethod;

            // Filtra callers preferencialmente que estejam em arquivos diferentes
            var filteredCallers = callers.Keys.Where(c => c.FilePath != currentMethod.FilePath).ToList();
            if (filteredCallers.Count == 0)
                filteredCallers = callers.Keys.ToList();

            if (filteredCallers.Count == 0)
                return currentMethod;

            // Prioriza o método que esteja em um arquivo code-behind (geralmente .aspx.cs)
            MethodInfo chosen = filteredCallers.FirstOrDefault(m => m.FilePath.EndsWith(".aspx.cs", StringComparison.OrdinalIgnoreCase))
                                   ?? filteredCallers.First();
            return GetTopLevelMethod(chosen, callerMapping, visited);
        }

        /// <summary>
        /// Extrai o valor do argumento passado para a invocação.
        /// Se for literal, retorna o valor. Se for uma variável, tenta localizar sua atribuição dentro do método.
        /// </summary>
        static string GetArgumentValue(ArgumentSyntax argument, MethodDeclarationSyntax methodDecl)
        {
            if (argument.Expression is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText;
            }
            else if (argument.Expression is IdentifierNameSyntax identifier)
            {
                string variableName = identifier.Identifier.Text;
                // Procura declarações locais no método que atribuam um valor à variável
                var declaration = methodDecl.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(v => v.Identifier.Text == variableName && v.Initializer != null);
                if (declaration != null)
                {
                    if (declaration.Initializer.Value is LiteralExpressionSyntax lit)
                        return lit.Token.ValueText;
                    else
                        return declaration.Initializer.Value.ToString();
                }
                return $"Variável: {variableName}";
            }
            else if (argument.Expression is BinaryExpressionSyntax binary)
            {
                return binary.ToString();
            }
            else
            {
                return argument.Expression.ToString();
            }
        }

        /// <summary>
        /// Procura por um arquivo ASPX que contenha um controle com OnClick igual ao nome do método de evento.
        /// Se o controle não tiver texto interno (por exemplo, um ImageButton), retorna o código completo do controle.
        /// </summary>
        static AspxButtonInfo FindAspxButtonInfo(string eventHandlerMethod, string projectPath)
        {
            var aspxFiles = Directory.GetFiles(projectPath, "*.aspx", SearchOption.AllDirectories);
            foreach (var file in aspxFiles)
            {
                string content = File.ReadAllText(file);
                // Expressão regular que busca controles ASP (ex.: LinkButton, ImageButton, etc.) com o OnClick desejado
                string pattern = $@"<asp:\w+[^>]*OnClick\s*=\s*""{Regex.Escape(eventHandlerMethod)}""[^>]*>(.*?)<\/asp:\w+>";
                var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string buttonText = match.Groups[1].Value.Trim();
                    // Se não houver texto interno, usa o código completo do controle
                    if (string.IsNullOrEmpty(buttonText))
                        buttonText = match.Value.Trim();
                    return new AspxButtonInfo { FilePath = file, ButtonText = buttonText };
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Classe que armazena as informações de cada invocação de API encontrada.
    /// </summary>
    class ApiInvocationInfo
    {
        public string ApiFile { get; set; }
        public string ApiMethodCalled { get; set; }
        public string EndpointArgument { get; set; }
        public MethodInfo ImmediateMethod { get; set; }
        public MethodInfo TopLevelMethod { get; set; }
        public AspxButtonInfo AspxButtonInfo { get; set; }
    }

    /// <summary>
    /// Representa as informações de um método (nome e caminho do arquivo).
    /// </summary>
    class MethodInfo : IEquatable<MethodInfo>
    {
        public string MethodName { get; set; }
        public string FilePath { get; set; }

        public override bool Equals(object obj) => Equals(obj as MethodInfo);
        public bool Equals(MethodInfo other) => other != null && MethodName == other.MethodName && FilePath == other.FilePath;
        public override int GetHashCode() => (MethodName + FilePath).GetHashCode();
    }

    /// <summary>
    /// Armazena as informações de um controle ASPX (arquivo e texto do botão).
    /// </summary>
    class AspxButtonInfo
    {
        public string FilePath { get; set; }
        public string ButtonText { get; set; }
    }
}
