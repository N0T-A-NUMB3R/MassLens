namespace MassLens.Demo.Contracts;

public record SubmitOrder(Guid OrderId, string CustomerId, string[] Items, decimal Total);
public record OrderSubmitted(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset SubmittedAt);
public record ProcessPayment(Guid OrderId, string CustomerId, decimal Amount, string PaymentMethod);
public record PaymentProcessed(Guid OrderId, decimal Amount, DateTimeOffset ProcessedAt);
public record PaymentFailed(Guid OrderId, string Reason);
public record ReserveInventory(Guid OrderId, string[] Items);
public record InventoryReserved(Guid OrderId, string[] Items);
public record InventoryUnavailable(Guid OrderId, string[] MissingItems);
public record ShipOrder(Guid OrderId, string CustomerId, string[] Items);
public record OrderShipped(Guid OrderId, string TrackingCode, DateTimeOffset ShippedAt);
public record SendNotification(Guid OrderId, string CustomerId, string Message, string Channel);
public record NotificationSent(Guid OrderId, string Channel);
public record CancelOrder(Guid OrderId, string Reason);
public record OrderCancelled(Guid OrderId, string Reason, DateTimeOffset CancelledAt);

// Inventory replenishment (separate flow, high volume)
public record CheckStock(string Sku, int RequiredQuantity);
public record StockChecked(string Sku, bool Available, int CurrentQuantity);

// Analytics events (fire-and-forget, very high volume)
public record TrackEvent(string EventType, string EntityId, Dictionary<string, string> Properties);
