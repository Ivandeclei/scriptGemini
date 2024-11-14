using ClosedXML.Excel;
using System.Text;
using System.Text.Json;

class Programa
{
    const string TOKEN = "COLOCAR TOKEN AQUI";
    public static async Task Main()
    {
        Console.WriteLine("Iniciando com Prompt Genérico");
        await GerarCSVPromptGenerico();
        Console.WriteLine("Finalizando com Prompt Genérico");

        Console.WriteLine("Iniciando com Prompt Específico");
        await GerarCSVPromptEspecifico();
        Console.WriteLine("Finalizando com Prompt Específico");
    }

    public static async Task GerarCSVPromptGenerico()
    {
        string caminhoArquivoExcel = @"C:\temp\read\MLCQCodeSmellSamples.xlsx";
        string caminhoArquivoCSV = @"C:\temp\arquivoPromptGenerico.csv";
        string mensagemAdicionar = "I need to check if the Java code below contains code smells (aka bad  smells). Could you please identify which smells occur in the following  code? However, do not describe the smells, just list them.  Please start your answer with “YES I found bad smells” when you find  any bad smell. Otherwise, start your answer with “NO, I did not find  any bad smell”.  When you start to list the detected bad smells, always put in your  answer “the bad smells are:” amongst the text your answer and always  separate it in this format: 1. Long method, 2. Feature envy";

        await ConsultarGemini(caminhoArquivoExcel, caminhoArquivoCSV, mensagemAdicionar);
    }

    public static async Task GerarCSVPromptEspecifico()
    {
        string caminhoArquivoExcel = @"C:\temp\read\MLCQCodeSmellSamples.xlsx";
        string caminhoArquivoCSV = @"C:\temp\arquivoPromptEspecifico.csv";
        string mensagemAdicionar = "The list below presents common code smells (aka bad smells). I need to  check if the Java code provided at the end of the input contains at least  one of them.  * Blob  * Data Class  * Feature Envy  * Long Method  Could you please identify which smells occur in the following code?  However, do not describe the smells, just list them.  Please start your answer with “YES I found bad smells” when you find  any bad smell. Otherwise, start your answer with “NO, I did not find  any bad smell”.  When you start to list the detected bad smells, always put in your  answer “the bad smells are:” amongst the text your answer and always  separate it in this format: 1. Long method, 2. Feature envy ...";

        await ConsultarGemini(caminhoArquivoExcel, caminhoArquivoCSV, mensagemAdicionar);
    }

    public static async Task ConsultarGemini(string caminhoPlanilha, string caminhoCsv, string prompt)
    {
        string urlApi = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent?key="+TOKEN;

        try
        {
            using (var planilha = new XLWorkbook(caminhoPlanilha))
            {
                var aba = planilha.Worksheet(1);
                var linhaCabecalho = aba.FirstRowUsed();
                int colunaId = 0;
                int colunaLink = 0;

                foreach (var celula in linhaCabecalho.Cells())
                {
                    string cabecalho = celula.GetString().Trim().ToLower();
                    if (cabecalho == "id") colunaId = celula.Address.ColumnNumber;
                    else if (cabecalho == "link") colunaLink = celula.Address.ColumnNumber;
                }

                if (colunaId == 0 || colunaLink == 0)
                {
                    Console.WriteLine("As colunas 'id' e/ou 'link' não foram encontradas.");
                    return;
                }

                var conteudoCSV = new StringBuilder();
                conteudoCSV.AppendLine("id||Retorno");

                using (HttpClient cliente = new HttpClient())
                {
                    foreach (var linha in aba.RowsUsed().Skip(1))
                    {
                        var celulaId = linha.Cell(colunaId);
                        var celulaLink = linha.Cell(colunaLink);
                        string id = celulaId.GetValue<string>();
                        string link = celulaLink.GetValue<string>();

                        if (!string.IsNullOrWhiteSpace(link))
                        {
                            try
                            {

                                HttpResponseMessage resposta = await cliente.GetAsync(link);
                                resposta.EnsureSuccessStatusCode();
                                string conteudoUrl = await resposta.Content.ReadAsStringAsync();
                                conteudoUrl = conteudoUrl.Replace("\r", "").Replace("\n", "");
                                string conteudoModificado = $"{prompt} {conteudoUrl}";

                                var dadosJson = new
                                {
                                    contents = new[]
                                    {
                                    new
                                    {
                                        parts = new[] { new { text = conteudoModificado } }
                                    }
                                }
                                };
                                string jsonPayload = JsonSerializer.Serialize(dadosJson);
                                HttpContent conteudoHttp = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                                int maxTentativas = 5;
                                int tentativaAtual = 0;
                                bool sucesso = false;

                                while (!sucesso && tentativaAtual < maxTentativas)
                                {
                                    try
                                    {
                                        HttpResponseMessage respostaApi = await cliente.PostAsync(urlApi, conteudoHttp);
                                        if (respostaApi.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                        {
                                            tentativaAtual++;
                                            int delay = (int)Math.Pow(2, tentativaAtual) * 1000;
                                            Console.WriteLine($"Tentativa {tentativaAtual}: Limite de requisições excedido, esperando {delay / 1000} segundos...");
                                            await Task.Delay(delay);
                                        }
                                        else
                                        {
                                            respostaApi.EnsureSuccessStatusCode();
                                            string conteudoRespostaApi = await respostaApi.Content.ReadAsStringAsync();

                                            using JsonDocument doc = JsonDocument.Parse(conteudoRespostaApi);
                                            string cheirosDetectados = string.Empty;
                                            if (doc.RootElement.TryGetProperty("candidates", out JsonElement elementoCandidatos) &&
                                                elementoCandidatos[0].TryGetProperty("content", out JsonElement elementoConteudo) &&
                                                elementoConteudo.GetProperty("parts")[0].TryGetProperty("text", out JsonElement elementoTexto))
                                            {
                                                cheirosDetectados = elementoTexto.GetString();
                                            }

                                            id = EscaparParaCsv(id);
                                            cheirosDetectados = EscaparParaCsv(cheirosDetectados);
                                            conteudoCSV.AppendLine($"{id}||{cheirosDetectados}");
                                            sucesso = true;
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        tentativaAtual++;
                                        int delay = (int)Math.Pow(2, tentativaAtual) * 1000;
                                        Console.WriteLine($"Tentativa {tentativaAtual}: Falha na requisição, esperando {delay / 1000} segundos...");
                                        await Task.Delay(delay);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                id = EscaparParaCsv(id);
                                conteudoCSV.AppendLine($"{id}|| exceção no código");
                                Console.WriteLine(e.ToString());
                            }
                        }
                        else
                        {
                            id = EscaparParaCsv(id);
                            conteudoCSV.AppendLine($"{id}|| O GitHub não pode ser acessado ");
                        }
                    }
                }

                File.WriteAllText(caminhoCsv, conteudoCSV.ToString());
                Console.WriteLine("Arquivo CSV criado com sucesso!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ocorreu um erro: {ex.Message}");
        }
    }


    public static string EscaparParaCsv(string campo)
    {
        if (campo.Contains("||") || campo.Contains("\"") || campo.Contains("\n"))
        {
            campo = campo.Replace("\"", "\"\"");
            campo = $"\"{campo}\"";
        }
        return campo;
    }
}
