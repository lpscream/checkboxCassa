using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Configuration;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Onix.Devices.Drivers;

public class CheckboxCassa : CashRegisterBase, ICashRegisterBaseAddNew, ICashRegisterBaseNewPayment
{
	public enum EN_RoundMode
	{
		None,
		ROUND_10,
		ROUND_50
	}

	private sealed class CheckboxCassaCapabilities : CashRegisterCapabilities
	{
		private CheckboxCassa owner;

		private Payment[] supportedPayments;

		public override decimal MaxAmount => 999999.99m;

		public override int MaxNameLength => ((CashRegisterCapabilities)this).NameWidth * 2;

		public override decimal MaxPrice => 999999.99m;

		public override decimal MaxQuantity => 99999.999m;

		public override int MaxReceiptItems => 0;

		public override int MaxReceiptSales => 250;

		public override int MaxTaxGroup => 5;

		public override int NameWidth => _nameWidth;

		public override int MaxTextLength => 0;

		public override decimal MaxTotal => 999999.99m;

		public override CashRegisterOptions Options => (CashRegisterOptions)5439;

		public override Payment[] SupportedPayments
		{
			get
			{
				if (supportedPayments == null)
				{
					supportedPayments = (Payment[])(object)new Payment[3]
					{
						default(Payment),
						(Payment)1,
						(Payment)2
					};
				}
				return supportedPayments;
			}
		}

		public override bool IsDisplaySupport => false;

		public override int TextWidth => owner._text_width;

		internal CheckboxCassaCapabilities(CheckboxCassa owner)
		{
			this.owner = owner;
		}
	}

	internal int art;

	private static int _nameWidth = 100;

	private int _text_width = 29;

	private decimal total;

	private int receipt_payment_type;

	private decimal receipt_payment_amount;

	private PaymentInfo paymentInfo;

	private int receipt_id;

	private string footer = "";

	private string header = "";

	private string fakemode_text = "";

	private ReceiptMode mode = (ReceiptMode)2;

	private string IPaddress = "127.0.0.1:9200";

	private EN_RoundMode roundCashMode;

	private static NumberFormatInfo nfi = CultureInfo.InvariantCulture.NumberFormat;

	private static string[] taxes = new string[6] { "1", "2", "3", "4", "5", "6" };

	private bool cap_getted;

	private CashRegisterCapabilities _capabilities;

	private static string[] payments = new string[3] { "CASH", "CASH", "CASHLESS" };

	private static string[] paymentLabels = new string[3] { "Готівка", "Готівка", "КАРТКА" };

	private bool FakeMode => (((CashRegisterBase)this).Status & 0x10000) != 0;

	private CashRegisterCapabilities capabilities
	{
		get
		{
			if (_capabilities == null)
			{
				_capabilities = (CashRegisterCapabilities)(object)new CheckboxCassaCapabilities(this);
			}
			return _capabilities;
		}
		set
		{
			_capabilities = value;
		}
	}

	protected override bool CanPrintFreeTextInReceipt()
	{
		return false;
	}

	private void get_capabilit()
	{
		if (!cap_getted)
		{
			IPaddress = (string)((DeviceBase)this).Parameters.GetValue("host", typeof(string), (object)"127.0.0.1:9200");
			roundCashMode = (EN_RoundMode)((DeviceBase)this).Parameters.GetValue("round", typeof(int), (object)"0");
			cap_getted = true;
		}
	}

	private Uri GetUri(string suffix)
	{
		return new Uri(string.Format(nfi, "http://{0}/api/v1/{1}", new object[2] { IPaddress, suffix }));
	}

	private HttpStatusCode getHTTPpostresponse(Uri uri, byte[] content, out string response)
	{
		return getHTTPresponse(uri, "POST", content, out response);
	}

	private HttpStatusCode getHTTPgetresponse(Uri uri, out string response)
	{
		return getHTTPresponse(uri, "GET", null, out response);
	}

	private HttpStatusCode getHTTPpatchresponse(Uri uri, byte[] content, out string response)
	{
		return getHTTPresponse(uri, "PATCH", content, out response);
	}

	private HttpStatusCode getHTTPresponse(Uri uri, string method, byte[] content, out string resp)
	{
		HttpWebRequest httpWebRequest = WebRequest.Create(uri) as HttpWebRequest;
		httpWebRequest.Method = method;
		httpWebRequest.ServicePoint.Expect100Continue = false;
		httpWebRequest.UserAgent = "Onix.Devices";
		httpWebRequest.KeepAlive = false;
		httpWebRequest.Accept = "application/json";
		httpWebRequest.Headers.Add("X-Client-Name", "MoyMagazin");
		httpWebRequest.Headers.Add("X-Client-Version", "v1.01");
		if (content != null)
		{
			ToggleAllowUnsafeHeaderParsing(enable: true);
			httpWebRequest.ContentType = "Application/json;charset=UTF-8;";
			httpWebRequest.ContentLength = content.Length;
			Stream requestStream = httpWebRequest.GetRequestStream();
			requestStream.Write(content, 0, content.Length);
			requestStream.Close();
		}
		HttpWebResponse httpWebResponse;
		try
		{
			httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
		}
		catch (WebException ex)
		{
			httpWebResponse = (HttpWebResponse)ex.Response;
		}
		Stream responseStream = httpWebResponse.GetResponseStream();
		StreamReader streamReader = new StreamReader(responseStream);
		resp = streamReader.ReadToEnd();
		streamReader.Close();
		responseStream.Close();
		httpWebResponse.Close();
		return httpWebResponse.StatusCode;
	}

	public static bool ToggleAllowUnsafeHeaderParsing(bool enable)
	{
		Assembly assembly = Assembly.GetAssembly(typeof(SettingsSection));
		if (assembly != null)
		{
			Type type = assembly.GetType("System.Net.Configuration.SettingsSectionInternal");
			if (type != null)
			{
				object obj = type.InvokeMember("Section", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, new object[0]);
				if (obj != null)
				{
					FieldInfo field = type.GetField("useUnsafeHeaderParsing", BindingFlags.Instance | BindingFlags.NonPublic);
					if (field != null)
					{
						field.SetValue(obj, enable);
						return true;
					}
				}
			}
		}
		return false;
	}

	private void CheckSmena()
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		Uri uri = GetUri("shift");
		string response = "";
		HttpStatusCode hTTPgetresponse = getHTTPgetresponse(uri, out response);
		if (hTTPgetresponse != HttpStatusCode.OK)
		{
			uri = GetUri("shift/open");
			writetolog(uri.ToString(), "");
			hTTPgetresponse = getHTTPpostresponse(uri, null, out response);
			if (hTTPgetresponse != HttpStatusCode.OK)
			{
				throw new DeviceCommException(response);
			}
		}
	}

	protected override void InternalOpen()
	{
		get_capabilit();
		CheckSmena();
		((CashRegisterBase)this).SetStatus((CashRegisterStatus)0);
	}

	protected override void InternalClose()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Invalid comparison between Unknown and I4
		if ((int)((DeviceBase)this).State != 6 && (int)((DeviceBase)this).State != 7)
		{
			((CashRegisterBase)this).SetStatus((CashRegisterStatus)0);
		}
	}

	protected override void InternalResync()
	{
		((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)int.MinValue);
		try
		{
		}
		finally
		{
			((CashRegisterBase)this).ResetStatusBits((CashRegisterStatus)int.MinValue);
		}
	}

	public override CashRegisterCapabilities GetCapabilities()
	{
		get_capabilit();
		return capabilities;
	}

	protected override void InternalOpenReceipt(ReceiptMode mode)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		this.mode = mode;
		CashRegisterStatus val = (CashRegisterStatus)0;
		receipt_payment_amount = 0m;
		total = 0m;
		receipt_payment_type = 0;
		paymentInfo = null;
		footer = "";
		header = "";
		fakemode_text = "";
		try
		{
			if (!FakeMode)
			{
				string response = "";
				string text = string.Format(nfi, "{{\"departament\": \"1\"}}");
				Uri uri = GetUri("receipt");
				byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
				writetolog(uri.ToString(), text);
				HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, bytes, out response);
				if (hTTPpostresponse != HttpStatusCode.OK)
				{
					throw new DeviceCommException(response);
				}
				JavaScriptSerializer val2 = new JavaScriptSerializer();
				Dictionary<string, object> dictionary = val2.Deserialize<Dictionary<string, object>>(response);
				receipt_id = Convert.ToInt32(dictionary["id"]);
			}
			((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)(0xE80 | val));
		}
		catch
		{
			((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)8192);
			throw;
		}
	}

	private string GetJSONFromPaymentInfo()
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Invalid comparison between Unknown and I4
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Invalid comparison between Unknown and I4
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected I4, but got Unknown
		if (paymentInfo == null)
		{
			return "";
		}
		string text = "";
		if ((int)paymentInfo.Payment == 2 && paymentInfo.TransactionInfo != null)
		{
			string text2 = "";
			BankTerminalOperation txnType = paymentInfo.TransactionInfo.txnType;
			switch (txnType - 1)
			{
			case 0:
				text2 = "ПРОДАЖ";
				break;
			case 1:
				text2 = "ПОВЕРНЕННЯ";
				break;
			case 2:
				text2 = "АНУЛЯЦIЯ";
				break;
			}
			text = string.Format(",\"owner_name\":\"{0}\"\r\n,\"bank_name\":\"{1}\"\r\n,\"terminal\":\"{2}\"\r\n,\"card_mask\":\"{3}\"\r\n,\"payment_system\":\"{4}\"\r\n,\"auth_code\":\"{5}\"\r\n,\"rrn\":\"{6}\"\r\n,\"label\":\"{7}\"\r\n,\"code\":1\r\n,\"signVerif\":\"{8}\"\r\n", paymentInfo.TransactionInfo.merchant ?? "", paymentInfo.TransactionInfo.bankAcquirer ?? "", paymentInfo.TransactionInfo.terminalId ?? "", paymentInfo.TransactionInfo.pan ?? "", paymentInfo.TransactionInfo.issuerName ?? "", paymentInfo.TransactionInfo.approvalCode ?? "", paymentInfo.TransactionInfo.rrn ?? "", text2, paymentInfo.TransactionInfo.signVerif ? "true" : "false");
		}
		string round_mode = "";
		decimal num = do_round(paymentInfo.Amount, paymentInfo.Payment, ref round_mode) * 100m;
		return string.Format(nfi, "{{  \"payments\": [{{\"type\": \"{0}\",\"value\": {1}{3}, \"label\": \"{4}\", \"code\":{5} }}]\r\n  ,\"print\": true\r\n  {2}\r\n}}", payments[paymentInfo.Payment], num, round_mode, text, paymentLabels[paymentInfo.Payment], ((int)paymentInfo.Payment > 1) ? 1 : 0);
	}

	public decimal do_round(decimal what, Payment pmnt, ref string round_mode)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		decimal result = what;
		round_mode = "";
		if ((int)pmnt < 2)
		{
			switch (roundCashMode)
			{
			case EN_RoundMode.ROUND_10:
				result = Math.Round(what, 1);
				round_mode = $" ,\"rounding_mode\": \"{roundCashMode}\"";
				break;
			case EN_RoundMode.ROUND_50:
			{
				round_mode = $" ,\"rounding_mode\": \"{roundCashMode}\"";
				int num = (int)(what * 100m % 100m);
				result = ((num >= 1 && num <= 24) ? Math.Floor(what) : ((num >= 25 && num <= 49) ? (Math.Floor(what) + 0.50m) : ((num >= 51 && num <= 74) ? (Math.Floor(what) + 0.50m) : ((num < 75 || num > 99) ? what : Math.Ceiling(what)))));
				break;
			}
			}
		}
		return result;
	}

	private void writetolog(string uri, string text)
	{
	}

	protected override void InternalCloseReceipt()
	{
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string response = "";
			if (!FakeMode)
			{
				string text = GetJSONFromPaymentInfo();
				if (string.IsNullOrEmpty(text))
				{
					string round_mode = "";
					decimal num = do_round(receipt_payment_amount, (Payment)receipt_payment_type, ref round_mode) * 100m;
					text = string.Format(nfi, "{{\r\n  \"payments\": [{{\"type\": \"{0}\",\"value\": {1}, \"label\": \"{4}\", \"code\":{5}   }}]\r\n  ,\"print\": true\r\n  {2}\r\n}}", payments[receipt_payment_type], num, round_mode, "", paymentLabels[receipt_payment_type], (receipt_payment_type > 1) ? 1 : 0);
				}
				Uri uri = GetUri("receipt/done");
				byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
				writetolog(uri.ToString(), text);
				HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, bytes, out response);
				if (hTTPpostresponse != HttpStatusCode.OK)
				{
					throw new DeviceCommException(response);
				}
			}
			else
			{
				string text2 = string.Format(nfi, "{{\"content\":\"{0}\"}}", new object[1] { escapeJson(fakemode_text) });
				Uri uri2 = GetUri("receipt/document");
				byte[] bytes2 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text2);
				writetolog(uri2.ToString(), text2);
				HttpStatusCode hTTPpostresponse2 = getHTTPpostresponse(uri2, bytes2, out response);
				if (hTTPpostresponse2 != HttpStatusCode.OK)
				{
					throw new DeviceCommException(response);
				}
			}
			receipt_payment_amount = 0m;
			total = 0m;
			receipt_payment_type = 0;
			footer = "";
			header = "";
			fakemode_text = "";
			receipt_id = 0;
			((CashRegisterBase)this).ResetStatusBits((CashRegisterStatus)48);
		}
		catch
		{
			((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)8192);
			throw;
		}
	}

	protected override void InternalAbortReceipt()
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		Uri uri = GetUri("receipt");
		string resp = "";
		HttpStatusCode hTTPresponse = getHTTPresponse(uri, "DELETE", null, out resp);
		if (hTTPresponse != HttpStatusCode.OK)
		{
			throw new DeviceCommException(resp);
		}
		receipt_payment_amount = 0m;
		total = 0m;
		receipt_payment_type = 0;
		footer = "";
		receipt_id = 0;
		((CashRegisterBase)this).ResetStatusBits((CashRegisterStatus)48);
	}

	protected override void InternalPayment(Payment payment, decimal amount, out decimal change)
	{
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected I4, but got Unknown
		change = ((amount >= total) ? (amount - total) : 0m);
		writetolog("InternalPayment", $"amount={amount:0.00};total={total:0.00};change={change:0.00}");
		receipt_payment_amount = amount;
		receipt_payment_type = (int)payment;
		((CashRegisterBase)this).ResetStatusBits((CashRegisterStatus)6912);
	}

	public void InternalPaymentNew(PaymentInfo PaymentInfo, out decimal change)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected I4, but got Unknown
		change = 0m;
		paymentInfo = PaymentInfo;
		writetolog("InternalPaymentNew", $"amount={PaymentInfo.Amount:0.00};total={total:0.00};change={change:0.00}");
		receipt_payment_amount = PaymentInfo.Amount;
		receipt_payment_type = (int)PaymentInfo.Payment;
		((CashRegisterBase)this).ResetStatusBits((CashRegisterStatus)6912);
	}

	public decimal InternalRegisterSaleNew(ReceiptSaleItem article)
	{
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Invalid comparison between Unknown and I4
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		decimal num = 0m;
		string text = (article.Marka ?? "").Trim();
		string text2 = (article.NomenCode ?? "").Trim();
		string text3 = (article.BarCode ?? "").Trim();
		string json = (article.Name ?? "").TrimEnd(new char[0]);
		decimal num2 = decimal.Floor(article.Quantity * 1000.0m);
		decimal num3 = decimal.Floor(article.Price * 100.0m);
		num = decimal.Floor(num3 * num2);
		total += num2 / 1000m * (num3 / 100m);
		try
		{
			string response = "";
			if (!FakeMode)
			{
				string text4 = string.Format(nfi, "{{\r\n  \"code\": \"{0}\",\r\n  \"name\": \"{1}\",\r\n  \"price\": {2},\r\n  \"quantity\": {3},\r\n  \"taxes\": [{8}],{4}{5}{6}\r\n  \"is_return\": {7}\r\n}}", article.Code, escapeJson(json), num3, num2, string.IsNullOrEmpty(text3) ? "" : string.Format(nfi, "\"barcode\": \"{0}\",", new object[1] { escapeJson(text3) }), string.IsNullOrEmpty(text2) ? "" : string.Format(nfi, "\"uktzed\": \"{0}\",", new object[1] { escapeJson(text2) }), string.IsNullOrEmpty(text) ? "" : string.Format(nfi, "\"excise_barcodes\": [\"{0}\"],", new object[1] { escapeJson(text) }), ((int)mode == 1) ? "true" : "false", article.TaxGroup + 1);
				Uri uri = GetUri("receipt/good");
				byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text4);
				writetolog(uri.ToString(), text4);
				HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, bytes, out response);
				if (hTTPpostresponse != HttpStatusCode.OK)
				{
					throw new DeviceCommException(response);
				}
			}
			else
			{
				char c = 'А';
				string text5 = string.Format(nfi, "{0:#0.000} x {1:#0.00}\n{2}{3}{4}{5}", article.Quantity, article.Price, string.IsNullOrEmpty(text3) ? "" : (escapeJson(text3) + "\n"), string.IsNullOrEmpty(text) ? "" : string.Format(nfi, "Акц.марки: {0}\n", new object[1] { escapeJson(text) }), string.IsNullOrEmpty(text2) ? "" : string.Format(nfi, "Код УКТЗЕД {0}\n", new object[1] { escapeJson(text2) }), escapeJson(json));
				string text6 = string.Format(nfi, "{0}{1," + (_text_width - 1) + ":0.00}{2}", new object[3]
				{
					text5,
					article.Quantity * article.Price,
					(char)(c + (byte)article.TaxGroup)
				});
				freeText(text6);
			}
			((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)16384);
			return num;
		}
		catch
		{
			((CashRegisterBase)this).SetStatusBits((CashRegisterStatus)8192);
			throw;
		}
	}

	protected override void InternalFreeText(string text)
	{
		freeText(text);
	}

	private string escapeJson(string json)
	{
		return json.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"")
			.Replace("\t", "\\t");
	}

	private void freeText(string text, bool IsFooter = true)
	{
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		string text2 = "";
		string text3 = "";
		if (IsFooter)
		{
			footer += ((footer.Length == 0 || footer.EndsWith("\n")) ? "" : "\n");
			footer += text;
			text2 = "footer";
			text3 = footer;
		}
		else
		{
			header += ((header.Length == 0 || header.EndsWith("\n")) ? "" : "\n");
			header += text;
			text2 = "header";
			text3 = header;
		}
		if (!FakeMode)
		{
			if (text3.Length > 1024)
			{
				text3 = text3.Remove(1024);
			}
			string response = "";
			Uri uri = GetUri("receipt");
			string s = string.Format(nfi, "{{ \"{0}\": \"{1}\"}}", new object[2]
			{
				text2,
				escapeJson(text3)
			});
			byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s);
			HttpStatusCode hTTPpatchresponse = getHTTPpatchresponse(uri, bytes, out response);
			if (hTTPpatchresponse != HttpStatusCode.OK)
			{
				throw new DeviceCommException(response);
			}
		}
		else
		{
			fakemode_text = header + ((header.Length == 0 || footer.Length == 0) ? "" : "\n") + footer;
		}
	}

	protected override void InternalOpenDrawer()
	{
	}

	protected override void InternalDailyReport()
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		string response = "";
		Uri uri = GetUri("shift/xreport");
		writetolog(uri.ToString(), "");
		HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, null, out response);
		if (hTTPpostresponse != HttpStatusCode.OK)
		{
			throw new DeviceCommException(response);
		}
	}

	protected override void InternalFiscalClosure()
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		string response = "";
		Uri uri = GetUri("shift/close");
		writetolog(uri.ToString(), "");
		HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, null, out response);
		if (hTTPpostresponse != HttpStatusCode.OK)
		{
			throw new DeviceCommException(response);
		}
	}

	protected override void InternalServiceAmount(decimal amount)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		string response = "";
		Uri uri = GetUri("receipt/service");
		string text = string.Format(nfi, "{{\r\n  \"payment\": {{\r\n    \"type\": \"CASH\",\r\n    \"value\": {0}\r\n  }}\r\n}}", new object[1] { amount * 100m });
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
		writetolog(uri.ToString(), text);
		HttpStatusCode hTTPpostresponse = getHTTPpostresponse(uri, bytes, out response);
		if (hTTPpostresponse != HttpStatusCode.OK)
		{
			throw new DeviceCommException(response);
		}
	}

	protected override void InternalRegisterSale(string name, decimal quantity, decimal price, int taxGroup, out decimal amount)
	{
		amount = 0m;
	}
}
