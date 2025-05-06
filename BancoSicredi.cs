using Negocios.DAL;
using Negocios.Enums;
using Negocios.Models;
using Negocios.Static;
using System;
using System.Threading.Tasks;
using Vision.Controller.Negocios.Utils;

namespace BancoSicredi
{
    public class BancoSicredi
    {
        readonly LogDB logger;
        readonly BancoDAL bancoDAL = new BancoDAL();
        readonly PedidoDAL pedidoDAL = new PedidoDAL();
        readonly CarteiraDAL carteiraDAL = new CarteiraDAL();
        readonly PlanoContaDAL planoContaDAL = new PlanoContaDAL();
        readonly BancoConvenioDAL bancoConvenioDAL = new BancoConvenioDAL();
        readonly PedidoParcelaDAL pedidoParcelaDAL = new PedidoParcelaDAL();
        readonly ContasAReceberParcelaDAL contasAReceberParcelaDAL = new ContasAReceberParcelaDAL();

        public BancoSicredi(LogDB logger) => this.logger = logger;

        public async Task ConsultaEEfetuaBaixas()
        {
            // ---------------------------------------------------------------- //
            var convenios = bancoConvenioDAL.GetInfoApiConveniosBancoSicredi(); //
            // ---------------------------------------------------------------- //

            logger.Info(TipoOperacaoLog.Log, $"Consultando {convenios.Count} convênio(s)");
            var dataproc = DateTime.Today;
            bool apardirDia = false;

            foreach (var convenio in convenios)
            {
                var api = new BancoSicrediApi(
                    apiKey: convenio.CONV__ACESS_TOKEN,
                    username: convenio.CONV__DS_INTEGRACAO_GW_KEY,
                    password: convenio.CONV__DS_SECRET_KEY,
                    ambienteProducao: convenio.CONV__TP_AMBIENTE == "1",
                    cooperativa: convenio.BANCO__NR_AGENCIA,
                    codigoBeneficiario: convenio.CONV__CD_CEDENTE,
                    posto: convenio.BANCO__NR_AGENCIA_DV,
                    logger
                );

                logger.Info(TipoOperacaoLog.Log, api.GetLogString());

                if (!api.TodosParametrosPreenchidos())
                {
                    continue;
                }

                dataproc = DateTime.Today;
                apardirDia = false;

                ReprocessaConvenio reprocessa =  new ReprocessaConvenio();
                reprocessa = bancoConvenioDAL.BuscaDataProcessamento(748);

                if (reprocessa != null)
                {
                    if (reprocessa.DT_SOMENTE_DIA != null)
                    {
                        dataproc = (DateTime)reprocessa.DT_SOMENTE_DIA;
                    }
                    else if (reprocessa.DT_APARTIR_DIA != null)
                    {
                        dataproc = (DateTime)reprocessa.DT_APARTIR_DIA;
                        apardirDia = true;
                    }

                    //bancoConvenioDAL.RemoveDataProcessamento(748);
                }
                var data = dataproc;
                bool trocaDia = true;
                while (trocaDia)
                {
                    // ------------------------------------------------------- //
                    var baixas = await api.BuscaBoletosLiquidadosPorDia(data); //
                    // ------------------------------------------------------- //
                    logger.Info(TipoOperacaoLog.Log, $"Boletos retornados para dia {data:dd/MM/yyyy}: {baixas.Count}");

                    foreach (var baixa in baixas)
                    {
                        // formato é {enum:OrigemCobranca:1}{cd_pedido/cd_lancamento}{nr_numeroparcela:2}
                        // então terá ao menos 4 caracteres
                        if (baixa.seuNumero.Length < 4)
                        {
                            logger.Info(TipoOperacaoLog.Log, $"{nameof(baixa.seuNumero)}={baixa.seuNumero} deve ter ao menos 6 caracteres");
                            continue;
                        }

                        var origem = baixa.seuNumero[0];
                        long codigoPDOuCR = 0;
                        long numeroParcela = 0;

                        if (origem != 'P' && origem != 'C')
                        {
                            logger.Info(TipoOperacaoLog.Log, $"{nameof(baixa.seuNumero)}={baixa.seuNumero},{nameof(origem)}={origem} não é P nem C");
                            continue;
                        }
                        // PD - CR

                        if ((baixa.seuNumero.Left(1) == "P") || (baixa.seuNumero.Left(1) == "C"))
                        {
                            var seuNumero = baixa.seuNumero;

                            if (seuNumero.Length == 4)
                            {
                                seuNumero = seuNumero.Insert(1, "00");


                                var codigoStr = seuNumero.Substring(1, 3);
                                var parcelaStr = seuNumero.Substring(seuNumero.Length - 2, 2);
                                codigoPDOuCR = Convert.ToInt64(codigoStr);
                                numeroParcela = Convert.ToInt64(parcelaStr);
                            }
                        }
                        if ((baixa.seuNumero.Left(2) == "PD") || (baixa.seuNumero.Left(2) == "CR"))
                        {
                            codigoPDOuCR = Convert.ToInt64(baixa.seuNumero.Substring(2, 6));
                            numeroParcela = Convert.ToInt64(baixa.seuNumero.Substring(baixa.seuNumero.Length - 2, 2));
                        }
                        //------------------------------------------------------------------------------------------------------------------------------
                        if (codigoPDOuCR == 0)
                        {
                            var codigoPDOuCRStr = baixa.seuNumero.Substring(1, baixa.seuNumero.Length - 1 - 2);
                            codigoPDOuCR = VSBaseNum.Decode(codigoPDOuCRStr, baseNum: 36);
                            numeroParcela = Convert.ToInt64(baixa.seuNumero.Substring(baixa.seuNumero.Length - 2, 2));
                        }

                        if (codigoPDOuCR == 0)
                        {
                            logger.Info(TipoOperacaoLog.Log, $"{nameof(baixa.seuNumero)}={baixa.seuNumero},{nameof(codigoPDOuCR)}={codigoPDOuCR} código do lançamento não é um inteiro válido");
                            continue;
                        }
                        //------------------------------------------------------------------------------------------------------------------------------
                        if (numeroParcela == 0)
                        {
                            var numeroParcelaStr = baixa.seuNumero.Substring(baixa.seuNumero.Length - 2);
                            numeroParcela = Convert.ToInt32(VSBaseNum.Decode(numeroParcelaStr, baseNum: 36));
                        }

                        if (numeroParcela == 0)
                        {
                            logger.Info(TipoOperacaoLog.Log, $"{nameof(baixa.seuNumero)}={baixa.seuNumero},{nameof(numeroParcela)}={numeroParcela} número da parcela não é um inteiro válido");
                            continue;
                        }
                        //------------------------------------------------------------------------------------------------------------------------------

                        if (origem == 'P')
                        {
                            var pedidoParcela = pedidoParcelaDAL.GetParcelaPorCodigoPedido(codigoPDOuCR, numeroParcela);

                            if (pedidoParcela == null)
                            {
                                logger.Info(TipoOperacaoLog.Log, $"{nameof(codigoPDOuCR)}={codigoPDOuCR},{nameof(numeroParcela)}={numeroParcela} PD não encontrado");
                                continue;
                            }


                            BaixaPD(pedidoParcela, baixa);
                            continue;
                        }

                        if (origem == 'C')
                        {
                            var contaAReceberParcela = contasAReceberParcelaDAL.GetParcelaPorCodigoLancamento(codigoPDOuCR, numeroParcela);

                            if (contaAReceberParcela == null)
                            {
                                logger.Info(TipoOperacaoLog.Log, $"{nameof(codigoPDOuCR)}={codigoPDOuCR},{nameof(numeroParcela)}={numeroParcela} CR não encontrada");
                                continue;
                            }

                            BaixaCR(contaAReceberParcela, baixa);
                            continue;
                        }
                    }

                    if (!apardirDia)
                    {
                        trocaDia = false;
                    }
                    else
                    {
                        if (data < DateTime.Today)
                        {
                            data = data.AddDays(1);
                        }

                        if (data == DateTime.Today)
                        {
                            apardirDia = false;
                        }
                    }

                }

            }
            bancoConvenioDAL.RemoveDataProcessamento(748);

        }

        private void BaixaPD(PedidoParcela pedidoParcela, BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item baixa)
        {
            if (pedidoDAL.ParcelaJaBaixada(pedidoParcela.CodigoPedido, pedidoParcela.NumeroParcela))
            {
                logger.Info(
                    TipoOperacaoLog.Log,
                    $"Parcela PD já baixada: " +
                    $"{nameof(pedidoParcela.CodigoPedido)}={pedidoParcela.CodigoPedido}," +
                    $"{nameof(pedidoParcela.NumeroParcela)}={pedidoParcela.NumeroParcela}"
                );
                return;
            }

            var pedido = pedidoDAL.GetPorId(pedidoParcela.CodigoPedido);
            var planoConta = planoContaDAL.GetPorCodigoEntidade(pedido.CodigoCliente);
            var cdContaContabil = planoConta?.Codigo ?? 0;

            if (cdContaContabil == 0)
            {
                planoConta = planoContaDAL.GetPorCodigoCarteira(pedido.CodigoCarteira);
                cdContaContabil = planoConta?.Codigo ?? 0;
            }

            var dsContaContabil = cdContaContabil > 0 ? planoConta?.DescricaoPlanoConta : "NÃO INFORMADO.";
            var cdBanco = pedidoDAL.GetCodigoBancoPrevisao(pedidoParcela.CodigoPedido, pedidoParcela.NumeroParcela);
            var dsBanco = bancoDAL.GetPorId(cdBanco).DescricaoBanco;

            if (string.IsNullOrEmpty(dsBanco))
                dsBanco = "NÃO INFORMADO.";

            var dsCarteira = carteiraDAL.GetPorId(pedido.CodigoCarteira).DescricaoCarteira;

            var (codigoBaixa, erroBaixa) = pedidoDAL.InserirBaixaPedido(
                CD_PEDIDO: pedidoParcela.CodigoPedido,
                NR_PARCELA: pedidoParcela.NumeroParcela,
                DT_PAGAMENTO: baixa.dataPagamento,
                VL_PAGAMENTO: Convert.ToDecimal(baixa.valorLiquidado),
                VL_JURO: Convert.ToDecimal(baixa.jurosLiquido + baixa.multaLiquida),
                CD_CONTA: cdContaContabil.ToString(),
                DS_CONTA: dsContaContabil,
                CD_BANCO: cdBanco,
                DS_BANCO: dsBanco,
                CD_CARTEIRA: pedido.CodigoCarteira,
                DS_CARTEIRA: dsCarteira
            );

            var strInfoCobrancaLog =
                $"{nameof(baixa.jurosLiquido)}={baixa.jurosLiquido}, " +
                $"{nameof(baixa.multaLiquida)}={baixa.multaLiquida}, " +
                $"{nameof(baixa.dataPagamento)}={baixa.dataPagamento}, " +
                $"{nameof(baixa.valorLiquidado)}={baixa.valorLiquidado}, " +
                $"{nameof(codigoBaixa)}={codigoBaixa}, " +
                $"{nameof(erroBaixa)}={erroBaixa}"
            ;

            if (!string.IsNullOrEmpty(erroBaixa))
            {
                logger.Info(TipoOperacaoLog.BaixarPedido, $"Erro ao inserir baixa: {strInfoCobrancaLog}");

                return;
            }

            var baixaUpdatedRows = pedidoDAL.AtualizaBaixaPedido(pedidoParcela.CodigoPedido, Convert.ToInt32(pedidoParcela.NumeroParcela));

            logger.Info(TipoOperacaoLog.BaixarPedido, $"Pedido baixado com sucesso: {strInfoCobrancaLog}, {nameof(baixaUpdatedRows)}={baixaUpdatedRows}");
        }

        private void BaixaCR(ContasAReceberParcela contaAReceberParcela, BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item baixa)
        {
            if (contaAReceberParcela.CodigoLancamento != null) 
            if (contasAReceberParcelaDAL.ParcelaJaBaixada(contaAReceberParcela.CodigoLancamento, contaAReceberParcela.NumeroParcela))
            {
                logger.Info(
                    TipoOperacaoLog.Log,
                    $"Parcela CR já baixada: " +
                    $"{nameof(contaAReceberParcela.CodigoLancamento)}={contaAReceberParcela.CodigoLancamento}," +
                    $"{nameof(contaAReceberParcela.NumeroParcela)}={contaAReceberParcela.NumeroParcela}"
                );
                return;
            }

            var planoConta = planoContaDAL.GetPorCodigoEntidade(contaAReceberParcela.Cliente);
            var cdContaContabil = planoConta?.Codigo ?? 0;

            if (cdContaContabil == 0)
            {
                planoConta = planoContaDAL.GetPorCodigoCarteira(contaAReceberParcela.Cliente);
                cdContaContabil = planoConta?.Codigo ?? 0;
            }

            var dsContaContabil = cdContaContabil > 0 ? planoConta?.DescricaoPlanoConta : "NÃO INFORMADO.";
            var cdBanco = contasAReceberParcelaDAL.GetCodigoBancoPrevisaoPagamento(contaAReceberParcela.CodigoLancamento, contaAReceberParcela.NumeroParcela);
            var dsBanco = bancoDAL.GetPorId(cdBanco).DescricaoBanco;

            if (string.IsNullOrEmpty(dsBanco))
                dsBanco = "NÃO INFORMADO.";

            var dsCarteira = carteiraDAL.GetPorId(contaAReceberParcela.Carteira).DescricaoCarteira;

            var (codigoBaixa, erroBaixa) = contasAReceberParcelaDAL.InserirBaixaBoleto(
                CD_LANCAMENTO: contaAReceberParcela.CodigoLancamento,
                NR_PARCELA: Convert.ToString(contaAReceberParcela.NumeroParcela),
                DT_PAGAMENTO: baixa.dataPagamento,
                VL_PAGAMENTO: Convert.ToDecimal(baixa.valorLiquidado),
                VL_JURO: Convert.ToDecimal(baixa.jurosLiquido + baixa.multaLiquida),
                CD_CONTA: cdContaContabil.ToString(),
                DS_CONTA: dsContaContabil,
                CD_BANCO: cdBanco,
                DS_BANCO: dsBanco,
                CD_CARTEIRA: contaAReceberParcela.Carteira,
                DS_CARTEIRA: dsCarteira
            );

            var strInfoCobrancaLog =
                $"{nameof(baixa.jurosLiquido)}={baixa.jurosLiquido}, " +
                $"{nameof(baixa.multaLiquida)}={baixa.multaLiquida}, " +
                $"{nameof(baixa.dataPagamento)}={baixa.dataPagamento}, " +
                $"{nameof(baixa.valorLiquidado)}={baixa.valorLiquidado}, " +
                $"{nameof(codigoBaixa)}={codigoBaixa}, " +
                $"{nameof(erroBaixa)}={erroBaixa}"
            ;

            if (!string.IsNullOrEmpty(erroBaixa))
            {
                logger.Info(TipoOperacaoLog.BaixarContaAReceber, $"Erro ao inserir baixa: {strInfoCobrancaLog}");

                return;
            }

            var baixaUpdatedRows = contasAReceberParcelaDAL.AtualizaBaixa(contaAReceberParcela.CodigoLancamento, contaAReceberParcela.NumeroParcela);

            logger.Info(TipoOperacaoLog.BaixarContaAReceber, $"CR baixada com sucesso: {strInfoCobrancaLog}, {nameof(baixaUpdatedRows)}={baixaUpdatedRows}");
        }
    }
}
