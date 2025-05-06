using Negocios.Enums;
using Negocios.Static;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BancoSicredi
{
    class Program
    {
        
        static async Task Main()
        {
            const int QTD_TENTATIVAS = 3;

            for (int i = 1; i <= QTD_TENTATIVAS; i++)
            {
                var logger = new LogDB(AppDomain.CurrentDomain.BaseDirectory, "Banco_Sicredi");
                
                logger.Info(TipoOperacaoLog.Log, $"teste inicio");

                try
                {
                    var banco = new BancoSicredi(logger);

                    await banco.ConsultaEEfetuaBaixas();

                    break;
                }
                catch (Exception ex)
                {
                    const int SEGUNDOS_ESPERA = 10;

                    logger.Info(
                        TipoOperacaoLog.Iniciar,
                        $"Erro ao executar processos, tentativa {i} de {QTD_TENTATIVAS}, " +
                        $"aguardando {SEGUNDOS_ESPERA}s: {ex.Message}"
                    );

                    Thread.Sleep(SEGUNDOS_ESPERA * 1000);
                }
            }
        }
    }
}
