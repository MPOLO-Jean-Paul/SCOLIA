package com.jpservices.scolia.flutterbridge;

/** Interface destinée au binding .NET. Le callback est asynchrone : complete() répond sur le thread Android. */
public interface CommandHandler {
    void onCommand(String requestId, String operation, String argumentsJson);
}
