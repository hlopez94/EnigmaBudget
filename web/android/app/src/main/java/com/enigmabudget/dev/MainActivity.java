package com.enigmabudget.dev;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(android.os.Bundle savedInstanceState) {
        registerPlugin(NativeAuthPlugin.class);
        super.onCreate(savedInstanceState);
    }
}
