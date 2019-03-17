using System;
using System.Threading;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Drawing;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using System.Reflection;
using HtmlAgilityPack;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    // ヘッダ付きテーブルのデータ
    class HtmlParsedTableData
    {
        public HtmlNode TdNode { get; }
        public string SimpleText { get; }

        public HtmlParsedTableData(HtmlNode node)
        {
            this.TdNode = node;
            this.SimpleText = node.GetSimpleText();
        }

        public string FirstHyperLinkTarget
        {
            get
            {
                var a_list = this.TdNode.Elements("a");
                foreach (var a in a_list)
                {
                    return a.GetAttributeValue("href", "");
                }
                return "";
            }
        }

        public override string ToString() => this.SimpleText;
    }

    // HTML をパースして読み込まれたヘッダ付きテーブル
    class HtmlParsedTableWithHeader
    {
        public HtmlNode TableNode { get; }
        public List<string> HeaderList { get; }
        public List<Dictionary<string, HtmlParsedTableData>> DataList { get; }

        public HtmlParsedTableWithHeader(HtmlNode table_node, string[] alternative_headers = null)
        {
            this.TableNode = table_node;

            var rows = TableNode.SelectNodes("tr");

            if (rows.Count <= 0) throw new ApplicationException("Table: rows.Count <= 0");

            // ヘッダリストの取得
            HeaderList = new List<string>();

            if (alternative_headers == null)
            {
                var header_coulmns = rows[0].SelectNodes("td");

                foreach (var column in header_coulmns)
                {
                    HeaderList.Add(column.GetSimpleText());
                }
            }
            else
            {
                HeaderList = alternative_headers.ToList();
            }

            // データリストの取得
            this.DataList = new List<Dictionary<string, HtmlParsedTableData>>();

            for (int i = 1; i < rows.Count; i++)
            {
                var td_list = rows[i].SelectNodes("td");


                Dictionary<string, HtmlParsedTableData> data_list = new Dictionary<string, HtmlParsedTableData>();

                if (td_list.Count != this.HeaderList.Count)
                {
                    throw new ApplicationException("td_list.Count != this.HeaderList.Count");
                }

                for (int j = 0; j < td_list.Count; j++)
                {
                    data_list[HeaderList[j]] = new HtmlParsedTableData(td_list[j]);
                }

                DataList.Add(data_list);
            }
        }
    }

    static class HtmlParser
    {
        public static HtmlDocument ParseHtml(string body)
        {
            HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(body);

            return doc;
        }
    }
}

namespace IPA.Cores.Helper.Basic
{
    static class HelperHtmlParser
    {
        public static string GetSimpleText(this HtmlNode node)
        {
            string str = node.InnerText.NonNullTrim();
            str = Str.FromHtml(str, true);
            return str;
        }

        public static HtmlParsedTableWithHeader ParseTable(this HtmlNode node) => new HtmlParsedTableWithHeader(node);
    }
}




