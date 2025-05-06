using System;
using System.Collections.Generic;

namespace BancoSicredi
{
    //{
    //	"items": [
    //		{
    //			"cooperativa": "0740",
    //			"codigoBeneficiario": "12817",
    //			"cooperativaPostoBeneficiario": "074018",
    //			"nossoNumero": "242194717",
    //			"seuNumero": "PD20759201",
    //			"tipoCarteira": "A",
    //			"dataPagamento": "2024-06-18 00:00:00.0",
    //			"valor": 122.22,
    //			"valorLiquidado": 130.42,
    //			"jurosLiquido": 4.53,
    //			"descontoLiquido": 0,
    //			"multaLiquida": 3.67,
    //			"abatimentoLiquido": 0,
    //			"tipoLiquidacao": "COMPE"
    //		}
    //	],
    //	"hasNext": false
    //}

    public class BancoSicrediApiDTO_BoletosLiquidadosPorDia
    {
        public List<BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item> items { get; set; }
        public bool hasNext { get; set; }
    }

    public class BancoSicrediApiDTO_BoletosLiquidadosPorDia_Item
    {
        public string tipoLiquidacao { get; set; }
        public string nossoNumero { get; set; }
        public string seuNumero { get; set; }
        public DateTime dataPagamento { get; set; }
        public double valor { get; set; }
        public double valorLiquidado { get; set; }
        public double multaLiquida { get; set; }
        public double jurosLiquido { get; set; }
    }
}
