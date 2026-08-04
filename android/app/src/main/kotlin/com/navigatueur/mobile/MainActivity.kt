package com.navigatueur.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.compose.viewModel
import com.navigatueur.mobile.ui.BrowserScreen
import com.navigatueur.mobile.ui.BrowserViewModel
import com.navigatueur.mobile.ui.theme.NavigatueurTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val app = application as NavigatueurApp
        val factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                BrowserViewModel(app.repository, app.adBlock) as T
        }

        setContent {
            NavigatueurTheme {
                val viewModel: BrowserViewModel = viewModel(factory = factory)
                BrowserScreen(viewModel = viewModel)
            }
        }
    }
}
