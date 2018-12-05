﻿using DotLiquid;
using Grand.Core;
using Grand.Core.Domain.Catalog;
using Grand.Core.Domain.Directory;
using Grand.Core.Domain.Localization;
using Grand.Core.Domain.Messages;
using Grand.Core.Domain.Orders;
using Grand.Core.Domain.Shipping;
using Grand.Core.Domain.Tax;
using Grand.Core.Infrastructure;
using Grand.Services.Catalog;
using Grand.Services.Common;
using Grand.Services.Customers;
using Grand.Services.Directory;
using Grand.Services.Helpers;
using Grand.Services.Localization;
using Grand.Services.Media;
using Grand.Services.Orders;
using Grand.Services.Payments;
using Grand.Services.Stores;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Grand.Services.Messages.DotLiquidDrops
{
    public partial class LiquidOrder : Drop
    {
        private Order _order;
        private string _languageId;
        private Language _language;
        private string _vendorId;
        private decimal _refundedAmount;
        private OrderNote _orderNote;
        private ICollection<LiquidOrderItem> _orderItems;
        private string _cusSubTotal;
        private bool _displaySubTotalDiscount;
        private string _cusSubTotalDiscount;
        private string _cusShipTotal;
        private string _cusPaymentMethodAdditionalFee;
        private bool _displayTax;
        private string _cusTaxTotal;
        private bool _displayTaxRates;
        private SortedDictionary<decimal, decimal> _taxRates;
        private bool _displayDiscount;
        private string _cusDiscount;
        private string _cusTotal;

        private readonly IAddressAttributeFormatter _addressAttributeFormatter;
        private readonly IPaymentService _paymentService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly IPriceFormatter _priceFormatter;
        private readonly ICurrencyService _currencyService;
        private readonly IDownloadService _downloadService;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IStoreService _storeService;
        private readonly IStoreContext _storeContext;
        private readonly ILanguageService _languageService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly MessageTemplatesSettings _templatesSettings;
        private readonly CatalogSettings _catalogSettings;
        private readonly TaxSettings _taxSettings;
        private readonly CurrencySettings _currencySettings;

        public LiquidOrder(IAddressAttributeFormatter addressAttributeFormatter,
            IPaymentService paymentService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            IPriceFormatter priceFormatter,
            ICurrencyService currencyService,
            IDownloadService downloadService,
            IProductAttributeParser productAttributeParser,
            IStoreService storeService,
            IStoreContext storeContext,
            ILanguageService languageService,
            IDateTimeHelper dateTimeHelper,
            MessageTemplatesSettings templatesSettings,
            CatalogSettings catalogSettings,
            TaxSettings taxSettings,
            CurrencySettings currencySettings)
        {
            this._addressAttributeFormatter = addressAttributeFormatter;
            this._paymentService = paymentService;
            this._localizationService = localizationService;
            this._workContext = workContext;
            this._priceFormatter = priceFormatter;
            this._currencyService = currencyService;
            this._downloadService = downloadService;
            this._productAttributeParser = productAttributeParser;
            this._storeService = storeService;
            this._storeContext = storeContext;
            this._languageService = languageService;
            this._dateTimeHelper = dateTimeHelper;
            this._templatesSettings = templatesSettings;
            this._catalogSettings = catalogSettings;
            this._taxSettings = taxSettings;
            this._currencySettings = currencySettings;
        }

        public void SetProperties(Order order, string languageId = "", string vendorId = "", OrderNote orderNote = null, decimal refundedAmount = 0)
        {
            this._order = order;
            this._languageId = languageId;
            this._vendorId = vendorId;
            this._orderNote = orderNote;
            this._refundedAmount = refundedAmount;
            this._language = _languageService.GetLanguageById(_languageId);

            this._orderItems = new List<LiquidOrderItem>();
            foreach (var orderItem in order.OrderItems)
            {
                var liquidOrderItem = new LiquidOrderItem();
                liquidOrderItem.SetProperties(orderItem, order, languageId);
                this._orderItems.Add(liquidOrderItem);
            }

            CalculateSubTotals();
        }

        private void CalculateSubTotals()
        {
            if (_order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax && !_taxSettings.ForceTaxExclusionFromOrderSubtotal)
            {
                //including tax

                //subtotal
                var orderSubtotalInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderSubtotalInclTax, _order.CurrencyRate);
                _cusSubTotal = _priceFormatter.FormatPrice(orderSubtotalInclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, true);
                //discount (applied to order subtotal)
                var orderSubTotalDiscountInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderSubTotalDiscountInclTax, _order.CurrencyRate);
                if (orderSubTotalDiscountInclTaxInCustomerCurrency > decimal.Zero)
                {
                    _cusSubTotalDiscount = _priceFormatter.FormatPrice(-orderSubTotalDiscountInclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, true);
                    _displaySubTotalDiscount = true;
                }
            }
            else
            {
                //exсluding tax

                //subtotal
                var orderSubtotalExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderSubtotalExclTax, _order.CurrencyRate);
                _cusSubTotal = _priceFormatter.FormatPrice(orderSubtotalExclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, false);
                //discount (applied to order subtotal)
                var orderSubTotalDiscountExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderSubTotalDiscountExclTax, _order.CurrencyRate);
                if (orderSubTotalDiscountExclTaxInCustomerCurrency > decimal.Zero)
                {
                    _cusSubTotalDiscount = _priceFormatter.FormatPrice(-orderSubTotalDiscountExclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, false);
                    _displaySubTotalDiscount = true;
                }
            }

            //shipping, payment method fee
            _taxRates = new SortedDictionary<decimal, decimal>();
            if (_order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax)
            {
                //including tax

                //shipping
                var orderShippingInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderShippingInclTax, _order.CurrencyRate);
                _cusShipTotal = _priceFormatter.FormatShippingPrice(orderShippingInclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, true);
                //payment method additional fee
                var paymentMethodAdditionalFeeInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.PaymentMethodAdditionalFeeInclTax, _order.CurrencyRate);
                _cusPaymentMethodAdditionalFee = _priceFormatter.FormatPaymentMethodAdditionalFee(paymentMethodAdditionalFeeInclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, true);
            }
            else
            {
                //excluding tax

                //shipping
                var orderShippingExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderShippingExclTax, _order.CurrencyRate);
                _cusShipTotal = _priceFormatter.FormatShippingPrice(orderShippingExclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, false);
                //payment method additional fee
                var paymentMethodAdditionalFeeExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.PaymentMethodAdditionalFeeExclTax, _order.CurrencyRate);
                _cusPaymentMethodAdditionalFee = _priceFormatter.FormatPaymentMethodAdditionalFee(paymentMethodAdditionalFeeExclTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, _language, false);
            }

            //shipping
            bool displayShipping = _order.ShippingStatus != ShippingStatus.ShippingNotRequired;

            //payment method fee
            bool displayPaymentMethodFee = _order.PaymentMethodAdditionalFeeExclTax > decimal.Zero;

            //tax
            _displayTax = true;
            _displayTaxRates = true;
            if (_taxSettings.HideTaxInOrderSummary && _order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax)
            {
                _displayTax = false;
                _displayTaxRates = false;
            }
            else
            {
                if (_order.OrderTax == 0 && _taxSettings.HideZeroTax)
                {
                    _displayTax = false;
                    _displayTaxRates = false;
                }
                else
                {
                    _taxRates = new SortedDictionary<decimal, decimal>();
                    foreach (var tr in _order.TaxRatesDictionary)
                        _taxRates.Add(tr.Key, _currencyService.ConvertCurrency(tr.Value, _order.CurrencyRate));

                    _displayTaxRates = _taxSettings.DisplayTaxRates && _taxRates.Any();
                    _displayTax = !_displayTaxRates;

                    var orderTaxInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderTax, _order.CurrencyRate);
                    string taxStr = _priceFormatter.FormatPrice(orderTaxInCustomerCurrency, true, _order.CustomerCurrencyCode, false, _language);
                    _cusTaxTotal = taxStr;
                }
            }

            //discount
            _displayDiscount = false;
            if (_order.OrderDiscount > decimal.Zero)
            {
                var orderDiscountInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderDiscount, _order.CurrencyRate);
                _cusDiscount = _priceFormatter.FormatPrice(-orderDiscountInCustomerCurrency, true, _order.CustomerCurrencyCode, false, _language);
                _displayDiscount = true;
            }

            //total
            var orderTotalInCustomerCurrency = _currencyService.ConvertCurrency(_order.OrderTotal, _order.CurrencyRate);
            _cusTotal = _priceFormatter.FormatPrice(orderTotalInCustomerCurrency, true, _order.CustomerCurrencyCode, false, _language);
        }

        public string OrderNumber
        {
            get { return _order.OrderNumber.ToString(); }
        }

        public string CustomerFullName
        {
            get { return string.Format("{0} {1}", _order.BillingAddress.FirstName, _order.BillingAddress.LastName); }
        }

        public string CustomerEmail
        {
            get { return _order.BillingAddress.Email; }
        }

        public string BillingFirstName
        {
            get { return _order.BillingAddress.FirstName; }
        }

        public string BillingLastName
        {
            get { return _order.BillingAddress.LastName; }
        }

        public string BillingPhoneNumber
        {
            get { return _order.BillingAddress.PhoneNumber; }
        }

        public string BillingEmail
        {
            get { return _order.BillingAddress.Email; }
        }

        public string BillingFaxNumber
        {
            get { return _order.BillingAddress.FaxNumber; }
        }

        public string BillingCompany
        {
            get { return _order.BillingAddress.Company; }
        }

        public string BillingVatNumber
        {
            get { return _order.BillingAddress.VatNumber; }
        }

        public string BillingAddress1
        {
            get { return _order.BillingAddress.Address1; }
        }

        public string BillingAddress2
        {
            get { return _order.BillingAddress.Address2; }
        }

        public string BillingCity
        {
            get { return _order.BillingAddress.City; }
        }

        public string BillingStateProvince
        {
            get { return !String.IsNullOrEmpty(_order.BillingAddress.StateProvinceId) ? EngineContext.Current.Resolve<IStateProvinceService>().GetStateProvinceById(_order.BillingAddress.StateProvinceId).GetLocalized(x => x.Name) : ""; }
        }

        public string BillingZipPostalCode
        {
            get { return _order.BillingAddress.ZipPostalCode; }
        }

        public string BillingCountry
        {
            get { return !String.IsNullOrEmpty(_order.BillingAddress.CountryId) ? EngineContext.Current.Resolve<ICountryService>().GetCountryById(_order.BillingAddress.CountryId).GetLocalized(x => x.Name) : ""; }
        }

        public string BillingCustomAttributes
        {
            get { return _addressAttributeFormatter.FormatAttributes(_order.BillingAddress.CustomAttributes); }
        }

        public string ShippingMethod
        {
            get { return _order.ShippingMethod; }
        }

        public string ShippingAdditionDescription
        {
            get { return _order.ShippingOptionAttributeDescription; }
        }

        public string ShippingFirstName
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.FirstName : ""; }
        }

        public string ShippingLastName
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.LastName : ""; }
        }

        public string ShippingPhoneNumber
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.PhoneNumber : ""; }
        }

        public string ShippingEmail
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.Email : ""; }
        }

        public string ShippingFaxNumber
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.FaxNumber : ""; }
        }

        public string ShippingCompany
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.Company : ""; }
        }

        public string ShippingAddress1
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.Address1 : ""; }
        }

        public string ShippingAddress2
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.Address2 : ""; }
        }

        public string ShippingCity
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.City : ""; }
        }

        public string ShippingStateProvince
        {
            get { return _order.ShippingAddress != null && !String.IsNullOrEmpty(_order.ShippingAddress.StateProvinceId) ? EngineContext.Current.Resolve<IStateProvinceService>().GetStateProvinceById(_order.ShippingAddress.StateProvinceId).GetLocalized(x => x.Name) : ""; }
        }

        public string ShippingZipPostalCode
        {
            get { return _order.ShippingAddress != null ? _order.ShippingAddress.ZipPostalCode : ""; }
        }

        public string ShippingCountry
        {
            get { return _order.ShippingAddress != null && !String.IsNullOrEmpty(_order.ShippingAddress.CountryId) ? EngineContext.Current.Resolve<ICountryService>().GetCountryById(_order.ShippingAddress.CountryId).GetLocalized(x => x.Name) : ""; }
        }

        public string ShippingCustomAttributes
        {
            get { return _addressAttributeFormatter.FormatAttributes(_order.ShippingAddress != null ? _order.ShippingAddress.CustomAttributes : ""); }
        }

        public string PaymentMethod
        {
            get
            {
                var paymentMethod = _paymentService.LoadPaymentMethodBySystemName(_order.PaymentMethodSystemName);
                var paymentMethodName = paymentMethod != null ? paymentMethod.GetLocalizedFriendlyName(_localizationService, _workContext.WorkingLanguage.Id) : _order.PaymentMethodSystemName;
                return paymentMethodName;
            }
        }

        public string VatNumber
        {
            get { return _order.VatNumber; }
        }

        public string CustomValues
        {
            get
            {
                var sbCustomValues = new StringBuilder();
                var customValues = _order.DeserializeCustomValues();
                if (customValues != null)
                {
                    foreach (var item in customValues)
                    {
                        sbCustomValues.AppendFormat("{0}: {1}", WebUtility.HtmlEncode(item.Key), WebUtility.HtmlEncode(item.Value != null ? item.Value.ToString() : ""));
                        sbCustomValues.Append("<br />");
                    }
                }
                return sbCustomValues.ToString();
            }
        }

        public string Products
        {
            get { return ProductListToHtmlTable(_order, _languageId, _vendorId); }
        }

        public string CreatedOn
        {
            get
            {
                var language = _languageService.GetLanguageById(_languageId);
                if (language != null && !String.IsNullOrEmpty(language.LanguageCulture))
                {
                    var customer = EngineContext.Current.Resolve<ICustomerService>().GetCustomerById(_order.CustomerId);
                    if (customer != null)
                    {
                        DateTime createdOn = _dateTimeHelper.ConvertToUserTime(_order.CreatedOnUtc, TimeZoneInfo.Utc, _dateTimeHelper.GetCustomerTimeZone(customer));
                        return createdOn.ToString("D", new CultureInfo(language.LanguageCulture));
                    }
                }
                else
                {
                    return _order.CreatedOnUtc.ToString("D");
                }

                return "";
            }
        }

        public string OrderURLForCustomer
        {
            get { return string.Format("{0}orderdetails/{1}", GetStoreUrl(_order.StoreId), _order.Id); }
        }

        public string AmountRefunded
        {
            get
            {
                var primaryStoreCurrencyCode = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode;
                var refundedAmountStr = _priceFormatter.FormatPrice(_refundedAmount, true, primaryStoreCurrencyCode, false, _workContext.WorkingLanguage);
                return refundedAmountStr;
            }
        }

        public string NewNoteText
        {
            get { return _orderNote.FormatOrderNoteText(); }
        }

        public string OrderNoteAttachmentUrl
        {
            get
            {
                var order = EngineContext.Current.Resolve<IOrderService>().GetOrderById(_orderNote.OrderId);
                return string.Format("{0}download/ordernotefile/{1}", GetStoreUrl(order.StoreId), _orderNote.Id);
            }
        }

        public ICollection<LiquidOrderItem> OrderItems
        {
            get { return _orderItems; }
        }

        public bool DisplaySubTotalDiscount
        {
            get
            {
                return _displaySubTotalDiscount;
            }
        }

        public string SubTotalDiscount
        {
            get
            {
                return _cusSubTotalDiscount;
            }
        }

        public string SubTotal
        {
            get
            {
                return _cusSubTotal;
            }
        }

        public string Shipping
        {
            get
            {
                return _cusShipTotal;
            }
        }

        public string Tax
        {
            get
            {
                return _cusTaxTotal;
            }
        }

        public string Total
        {
            get
            {
                return _cusTotal;
            }
        }

        public bool DisplayShipping
        {
            get
            {
                return _order.ShippingStatus != ShippingStatus.ShippingNotRequired;
            }
        }

        public bool DisplayPaymentMethodFee
        {
            get
            {
                return _order.PaymentMethodAdditionalFeeExclTax > decimal.Zero;
            }
        }

        public string PaymentMethodAdditionalFee
        {
            get
            {
                return _cusPaymentMethodAdditionalFee;
            }
        }

        public bool DisplayTax
        {
            get
            {
                return _displayTax;
            }
        }

        public Dictionary<string, string> TaxRates
        {
            get
            {
                Dictionary<string, string> dict = new Dictionary<string, string>();

                foreach (var item in _taxRates)
                {
                    string taxRate = String.Format(_localizationService.GetResource("Messages.Order.TaxRateLine"), _priceFormatter.FormatTaxRate(item.Key));
                    string taxValue = _priceFormatter.FormatPrice(item.Value, true, _order.CustomerCurrencyCode, false, _language);
                    dict.Add(taxRate, taxValue);
                }

                return dict;
            }
        }

        public bool DisplayDiscount
        {
            get
            {
                return _displayDiscount;
            }
        }

        public string Discount
        {
            get
            {
                return _cusDiscount;
            }
        }
               
        protected virtual string GetStoreUrl(string storeId = "", bool useSsl = false)
        {
            var store = _storeService.GetStoreById(storeId) ?? _storeContext.CurrentStore;

            if (store == null)
                throw new Exception("No store could be loaded");

            return useSsl ? store.SecureUrl : store.Url;
        }
    }
}