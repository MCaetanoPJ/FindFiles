# Análise Estática de Chamadas de API em Projeto ASP.NET

Este repositório contém uma aplicação console em C# que realiza análise estática de um projeto ASP.NET. O código utiliza o [Roslyn](https://github.com/dotnet/roslyn) (Microsoft.CodeAnalysis) para processar os arquivos C# e identificar invocações de métodos que realizam chamadas para APIs. Além disso, a aplicação identifica recursivamente o método "pai" (top-level) que desencadeia o fluxo até a chamada da API e, ainda, localiza nos arquivos ASPX os controles que acionam esse método (por exemplo, botões) para extrair o texto exibido ou o código HTML completo (no caso de controles sem conteúdo textual).

---

## Objetivo

O propósito deste código é:

- **Mapear e identificar** as invocações de métodos que fazem chamadas para APIs (ex.: `GET_JWT`, `POST_JWT`, etc.) em um projeto ASP.NET.
- **Determinar o método "pai" (top-level)** que inicia o fluxo de execução (geralmente um método de evento associado a um controle ASPX).
- **Extrair o endpoint** passado para as chamadas de API, inclusive resolvendo o valor de variáveis definidas localmente.
- **Localizar controles ASPX** que possuam o atributo `OnClick` relacionado ao método de evento, extraindo o texto visível ou, caso não haja texto (como em `<asp:ImageButton>`), o HTML completo do controle.
- **Gerar um relatório consolidado** com todas as informações mapeadas, para auditoria, refatoração ou documentação.

---

## Tecnologias Utilizadas

- **.NET (C#)** – Aplicação Console.
- **Roslyn (Microsoft.CodeAnalysis)** – Para análise estática do código.
- **Paralelismo em C#** – Uso de `Parallel.ForEach` e coleções concorrentes (`ConcurrentBag`, `ConcurrentDictionary`) para otimização de performance.

---

## Estrutura do Código

### 1. Main()

- **Configuração:** Define o caminho do projeto/solution e o arquivo de saída (`ResultadoLocalizacao.txt`).
- **Especificação dos métodos de API:** Lista os métodos (ex.: `GET_JWT`, `POST_JWT`, etc.) que serão analisados.
- **Processamento dos Arquivos:** Obtém todos os arquivos `.cs` do projeto e os processa em paralelo para extrair as invocações de API.

### 2. Processamento de Arquivos C#

- **Leitura e Parsing:** Cada arquivo é lido e transformado em uma árvore de sintaxe com `CSharpSyntaxTree.ParseText`.
- **Extração de Invocações:** São filtradas as chamadas de métodos que correspondem aos métodos de API.
- **Construção do Call Graph:** Durante o parsing, é construído um dicionário concorrente (`callerMapping`) que relaciona cada método ao(s) método(s) que o chamam, facilitando a determinação do método top-level.

### 3. Extração do Endpoint

- A função `GetArgumentValue`:
  - **Literal:** Se o argumento for uma _string literal_, retorna seu valor.
  - **Variável:** Se for uma variável, busca no escopo do método (`MethodDeclarationSyntax`) a declaração e resolve o valor.
  - **Expressões Complexas:** Para expressões (como concatenação), retorna uma representação textual da expressão.

### 4. Determinação do Método Top-Level

- A função `GetTopLevelMethod` realiza uma busca recursiva utilizando o `callerMapping` para encontrar o método que **não é chamado por nenhum outro** (o método "pai").
- Utiliza um conjunto `visited` para evitar ciclos na recursividade.

### 5. Busca de Controles ASPX

- A função `FindAspxButtonInfo`:
  - Varre os arquivos `.aspx` para encontrar controles que possuam o atributo `OnClick` igual ao nome do método top-level.
  - **Extração do Texto:** Se o controle tiver conteúdo interno (como um `<asp:LinkButton>`), extrai o texto exibido.
  - **Controles sem Texto:** Se o controle não tiver conteúdo (como um `<asp:ImageButton>`), retorna o HTML completo do controle.

### 6. Geração do Relatório

- Todas as informações coletadas (arquivo da chamada, método da API, endpoint, método imediato, método pai e detalhes do controle ASPX) são compiladas em um relatório.
- O relatório é salvo em um arquivo de texto (`ResultadoLocalizacao.txt`) e também exibido no console.

---

## Otimizações Aplicadas

- **Processamento Paralelo:** Uso de `Parallel.ForEach` para processar simultaneamente os arquivos `.cs`, melhorando a performance em projetos grandes.
- **Coleções Concorrentes:** Utilização de `ConcurrentBag` e `ConcurrentDictionary` para acesso seguro a dados durante o processamento paralelo.
- **Construção do Call Graph:** Um índice único (`callerMapping`) evita varreduras repetitivas, otimizando a busca pelo método top-level.

---

## Como Executar

1. **Configuração do Caminho:**  
   - Atualize a variável `projectPath` no código com o diretório onde se encontra sua solution ou projeto ASP.NET.
   - O arquivo de saída `ResultadoLocalizacao.txt` será gerado neste diretório.

2. **Compilação e Execução:**  
   - Compile o projeto usando o Visual Studio ou via linha de comando.
   - Execute o executável da aplicação console. O relatório será exibido no console e salvo no caminho especificado.

---

## Conclusão

Este código auxilia desenvolvedores a:

- **Identificar e mapear** as chamadas de métodos que realizam requisições a APIs.
- **Localizar o método top-level** que inicia o fluxo de execução, geralmente associado a eventos de controles ASPX.
- **Extrair endpoints** passados para os métodos de API, resolvendo valores de variáveis quando necessário.
- **Associar controles ASPX** aos métodos de evento, extraindo o texto visível ou o HTML completo do controle.
- **Gerar um relatório detalhado** para fins de auditoria, refatoração ou documentação do fluxo de chamadas no sistema.

Utilizando o Roslyn e técnicas de processamento paralelo, a aplicação apresenta uma solução robusta e performática para a análise estática de projetos ASP.NET.

---

## Arquivos Relacionados

- **`Program.cs`** – Contém o código principal da aplicação console.
- **`ResultadoLocalizacao.txt`** – Arquivo gerado com o relatório detalhado após a execução.

---
