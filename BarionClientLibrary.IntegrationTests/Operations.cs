using System;
using System.Globalization;
using System.Linq;
using BarionClientLibrary.Operations.Common;
using BarionClientLibrary.Operations.Enums;
using BarionClientLibrary.Operations.FinishReservation;
using BarionClientLibrary.Operations.PaymentState;
using BarionClientLibrary.Operations.Refund;
using BarionClientLibrary.Operations.StartPayment;

namespace BarionClientLibrary.IntegrationTests;

internal sealed class Operations
{
    public const string POSTransactionId = "T1";

    public static GetPaymentStateOperationResult GetPaymentState(BarionClient barionClient, StartPaymentOperationResult result)
    {
        var paymentStateOperation = new GetPaymentStateOperation
        {
            PaymentId = result.PaymentId
        };

        var statusresult = barionClient.ExecuteAsync<GetPaymentStateOperationResult>(paymentStateOperation).Result;

        return !statusresult.IsOperationSuccessful ? throw new InvalidOperationException("Get payment state operation was not successful.") : statusresult; //TODO: Check if this Error type is correct.
    }

    public static StartPaymentOperationResult StartPayment(BarionClient barionClient, BarionSettings settings, PaymentType paymentType, TimeSpan? reservationPeriod = null, bool initiateRecurrence = false, string recurrenceId = null)
    {
        var startPaymentOperation = new StartPaymentOperation
        {
            GuestCheckOut = true,
            PaymentType = paymentType,
            ReservationPeriod = reservationPeriod,
            FundingSources = [FundingSourceType.All],
            PaymentRequestId = "P1",
            OrderNumber = "1_0",
            Currency = Currency.HUF,
            CallbackUrl = "http://index.hu",
            Locale = CultureInfo.CurrentCulture,
            RedirectUrl = "http://index.hu",
            InitiateRecurrence = initiateRecurrence,
            RecurrenceId = recurrenceId
        };

        var transaction = new PaymentTransaction
        {
            Payee = settings.Payee,
            POSTransactionId = POSTransactionId,
            Total = new decimal(1000),
            Comment = "comment"
        };

        var item = new Item
        {
            Name = "Test",
            Description = "Test",
            ItemTotal = new decimal(1000),
            Quantity = 1,
            Unit = "piece",
            UnitPrice = new decimal(1000),
            SKU = "SKU"
        };

        transaction.Items = [item];
        startPaymentOperation.Transactions = [transaction];

        Console.WriteLine("Sending StartPayment...");
        var result = barionClient.ExecuteAsync<StartPaymentOperationResult>(startPaymentOperation).Result;

        return !result.IsOperationSuccessful ? throw new InvalidOperationException("Start payment operation was not successful.") : result;
    }

    public static RefundOperationResult Refund(BarionClient barionClient, StartPaymentOperationResult result)
    {
        var refundOpertation = new RefundOperation
        {
            PaymentId = result.PaymentId
        };

        var transactionToRefund = new TransactionToRefund
        {
            TransactionId = result.Transactions.Single(t => t.POSTransactionId == POSTransactionId).TransactionId,
            AmountToRefund = new decimal(50)
        };
        refundOpertation.TransactionsToRefund = [transactionToRefund];

        Console.WriteLine("Sending Refund...");
        var refundResult = barionClient.ExecuteAsync<RefundOperationResult>(refundOpertation).Result;

        return !refundResult.IsOperationSuccessful ? throw new InvalidOperationException("Refund operation was not successful") : refundResult;//TODO: Check if this Error type is correct.
    }

    public static FinishReservationOperationResult FinishReservation(BarionClient barionClient, GetPaymentStateOperationResult beforeFinishReservationState)
    {
        var finishReservation = new FinishReservationOperation
        {
            PaymentId = beforeFinishReservationState.PaymentId
        };

        var transactionToFinish = new TransactionToFinish
        {
            TransactionId = beforeFinishReservationState.Transactions.Single(t => t.POSTransactionId == POSTransactionId).TransactionId,
            Total = 500
        };

        finishReservation.Transactions = [transactionToFinish];

        Console.WriteLine("Sending FinishReservation...");
        var finishReservationResult = barionClient.ExecuteAsync<FinishReservationOperationResult>(finishReservation).Result;

        return !finishReservationResult.IsOperationSuccessful
            ? throw new InvalidOperationException("Finish reservation operation was not successful.")//TODO: Check if this Error type is correct.
            : finishReservationResult;
    }
}
