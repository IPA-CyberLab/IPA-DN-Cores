// CoreUtil
// 
// Copyright (C) 1997-2010 Daiyuu Nobori. All Rights Reserved.
// Copyright (C) 2004-2010 SoftEther Corporation. All Rights Reserved.

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
using System.Net.Mail;
using System.Net.Mime;

namespace IPA.Cores.Basic
{
    public enum SmtpLanguage
    {
        Japanese,
        Simpled_Chinese,
        Engligh,
    }

    public class SmtpCharsetList
    {
        public readonly List<Encoding> CharsetList;

        public SmtpCharsetList(SmtpLanguage language)
        {
            CharsetList = new List<Encoding>();

            switch (language)
            {
                case SmtpLanguage.Japanese:
                    //CharsetList.Add(Str.ISO88591Encoding);
                    CharsetList.Add(Str.AsciiEncoding);
//                    CharsetList.Add(Str.ISO2022JPEncoding);
//                    CharsetList.Add(Str.GB2312Encoding);
                    CharsetList.Add(Str.Utf8Encoding);
                    break;

                case SmtpLanguage.Simpled_Chinese:
                    //CharsetList.Add(Str.ISO88591Encoding);
                    CharsetList.Add(Str.AsciiEncoding);
//                    CharsetList.Add(Str.GB2312Encoding);
//                    CharsetList.Add(Str.ISO2022JPEncoding);
                    CharsetList.Add(Str.Utf8Encoding);
                    break;

                case SmtpLanguage.Engligh:
                    //CharsetList.Add(Str.ISO88591Encoding);
                    CharsetList.Add(Str.AsciiEncoding);
                    CharsetList.Add(Str.Utf8Encoding);
//                    CharsetList.Add(Str.ISO2022JPEncoding);
//                    CharsetList.Add(Str.GB2312Encoding);
                    break;
            }
        }

        public Encoding GetAppropriateCharset(string text)
        {
            foreach (Encoding enc in CharsetList)
            {
                if (Str.IsSuitableEncodingForString(text, enc))
                {
                    return enc;
                }
            }

            return Str.Utf8Encoding;
        }

        public static TransferEncoding GetTransferEncoding(Encoding enc)
        {
            if (Str.StrCmpi(enc.WebName, "iso-2022-jp"))
            {
                return TransferEncoding.SevenBit;
            }
            else if (Str.StrCmpi(enc.WebName, "us-ascii"))
            {
                return TransferEncoding.SevenBit;
            }
            else if (Str.StrCmpi(enc.WebName, "iso-8859-1"))
            {
                return TransferEncoding.SevenBit;
            }
            else
            {
                return TransferEncoding.Base64;
            }
        }

        public string NormalizeMailAddress(MailAddress src)
        {
            string name = src.DisplayName;
            string addr = src.Address;

            if (Str.IsAscii(name) && Str.InStr(name, "\"") == false)
            {
                return string.Format("\"{0}\" <{1}>", name, addr);
            }
            else
            {
                Encoding enc = GetAppropriateCharset(name);
                byte[] data = enc.GetBytes(name);
                string text = string.Format("=?{0}?B?{1}?= <{2}>", enc.WebName.ToUpper(),
                    Convert.ToBase64String(data, Base64FormattingOptions.None), addr);
                return text;
            }
        }
    }

    public class SmtpBody
    {
        public string XMailer = Env.FrameworkInfoString;
        public string MimeOLE = "Produced By " + Env.FrameworkInfoString;
        public string MSMailPriority = "Normal";
        public string MailPriority = "3";
        public MailAddress From;
        public MailAddress To;
        public MailAddress ReplyTo = null;
        public string Subject;
        public string Body;
        public string BodyHtml = "";
        public SmtpCharsetList CharsetList;
        public List<Attachment> AttatchedFileList;
        public List<LinkedResource> LinkedResourceList;
        public List<MailAddress> CcList = new List<MailAddress>();
        public List<MailAddress> BccList = new List<MailAddress>();

        public SmtpBody(MailAddress from, MailAddress to, string subject, string body)
            : this(from, to, subject, body, new SmtpCharsetList(SmtpLanguage.Japanese))
        {
        }
        public SmtpBody(MailAddress from, MailAddress to, string subject, string body, SmtpCharsetList charsetList)
        {
            this.From = from;
            this.To = to;
            this.Subject = subject;
            this.Body = body;
            this.CharsetList = charsetList;
            this.AttatchedFileList = new List<Attachment>();
            this.LinkedResourceList = new List<LinkedResource>();
        }

        // リソースファイルを追加
        public void AddLinkedResourceFile(LinkedResource a)
        {
            this.LinkedResourceList.Add(a);
        }
        public void AddLinkedResourceFile(byte[] data, string contentType, string id)
        {
            MemoryStream ms = new MemoryStream(data);
            LinkedResource a = new LinkedResource(ms, contentType);
            if (Str.IsEmptyStr(contentType) == false)
            {
                a.ContentType = new ContentType(contentType);
            }
            if (Str.IsEmptyStr(id) == false)
            {
                a.ContentId = id;
            }
            a.TransferEncoding = TransferEncoding.Base64;
            //a.ContentLink = new Uri("http://www.softether.co.jp/" + id);

            AddLinkedResourceFile(a);
        }

        // 添付ファイルを追加
        public void AddAttachedFile(Attachment a)
        {
            this.AttatchedFileList.Add(a);
        }
        public void AddAttachedFile(byte[] data, string contentType, string name, string id)
        {
            MemoryStream ms = new MemoryStream(data);
            Attachment a = new Attachment(ms, contentType);
            if (Str.IsEmptyStr(contentType) == false)
            {
                a.ContentType = new ContentType(contentType);
            }
            if (Str.IsEmptyStr(id) == false)
            {
                a.ContentId = id;
            }
            a.TransferEncoding = TransferEncoding.Base64;
            if (Str.IsEmptyStr(name) == false)
            {
                a.Name = name;
                if (Str.IsAscii(name))
                {
                    a.NameEncoding = Str.AsciiEncoding;
                }
                else
                {
                    a.NameEncoding = Str.Utf8Encoding;
                }
            }

            AddAttachedFile(a);
        }

        // 送信
        public bool SendSafe(SmtpConfig smtp)
        {
            try
            {
                Send(smtp);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public void Send(SmtpConfig smtp)
        {
            SmtpClient c = new SmtpClient(smtp.SmtpServer, smtp.SmtpPort);
            c.DeliveryMethod = SmtpDeliveryMethod.Network;
            c.EnableSsl = smtp.UseSSL;

            MailMessage mail = new MailMessage(CharsetList.NormalizeMailAddress(this.From),
                CharsetList.NormalizeMailAddress(this.To));

            Encoding bodyEnc = CharsetList.GetAppropriateCharset(this.Body);
            TransferEncoding bodyTran = SmtpCharsetList.GetTransferEncoding(bodyEnc);

            byte[] bodyData = bodyEnc.GetBytes(this.Body);
            MemoryStream ms = new MemoryStream(bodyData);

            AlternateView alt = new AlternateView(ms,
                new ContentType("text/plain; charset=" + bodyEnc.WebName));

            alt.TransferEncoding = bodyTran;

            if (Str.IsEmptyStr(this.Body) == false)
            {
                mail.AlternateViews.Add(alt);
            }
            mail.Body = "";
            mail.BodyEncoding = bodyEnc;

            // HTML メールの場合
            if (Str.IsEmptyStr(this.BodyHtml) == false)
            {
                Encoding htmlEnc = CharsetList.GetAppropriateCharset(this.BodyHtml);
                TransferEncoding htmlTran = SmtpCharsetList.GetTransferEncoding(htmlEnc);

                byte[] htmlData = htmlEnc.GetBytes(this.BodyHtml);
                ms = new MemoryStream(htmlData);

                AlternateView alt2 = new AlternateView(ms,
                    new ContentType("text/html; charset=" + htmlEnc.WebName));

                // リソースファイル
                foreach (LinkedResource a in LinkedResourceList)
                {
                    alt2.LinkedResources.Add(a);
                }

                mail.AlternateViews.Add(alt2);
            }

            // 添付ファイル
            foreach (Attachment a in AttatchedFileList)
            {
                mail.Attachments.Add(a);
            }

            Encoding subjectEnc = CharsetList.GetAppropriateCharset(this.Subject);
            byte[] subjectData = subjectEnc.GetBytes(this.Subject);
            string subjectText = string.Format("=?{0}?B?{1}?=", subjectEnc.WebName.ToUpper(),
                Convert.ToBase64String(subjectData, Base64FormattingOptions.None));
            if (Str.IsAscii(this.Subject))
            {
                subjectText = this.Subject;
            }

            mail.Subject = subjectText;

            if (this.ReplyTo != null)
            {
//                mail.ReplyTo = this.ReplyTo;
                mail.ReplyToList.Add(this.ReplyTo);
            }

            foreach (MailAddress cc in CcList)
            {
                mail.CC.Add(cc);
            }

            foreach (MailAddress bcc in BccList)
            {
                mail.Bcc.Add(bcc);
            }

            mail.Headers.Add("X-Mailer", XMailer);
            mail.Headers.Add("X-MSMail-Priority", MSMailPriority);
            mail.Headers.Add("X-Priority", MailPriority);
            mail.Headers.Add("X-MimeOLE", MimeOLE);

            c.Send(mail);
        }
    }

    public class SmtpConfig
    {
        public string SmtpServer;
        public int SmtpPort;
        public bool UseSSL;

        public SmtpConfig(string smtpServer, int smtpPort)
        {
            this.SmtpServer = smtpServer;
            this.SmtpPort = smtpPort;
        }
    }
}
