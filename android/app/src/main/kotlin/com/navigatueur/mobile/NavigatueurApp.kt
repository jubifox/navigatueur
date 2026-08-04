package com.navigatueur.mobile

import android.app.Application
import com.navigatueur.mobile.data.AdBlockManager
import com.navigatueur.mobile.data.BrowserRepository

class NavigatueurApp : Application() {
    lateinit var repository: BrowserRepository
        private set

    lateinit var adBlock: AdBlockManager
        private set

    override fun onCreate() {
        super.onCreate()
        repository = BrowserRepository(this)
        adBlock = AdBlockManager.load(this)
    }
}
