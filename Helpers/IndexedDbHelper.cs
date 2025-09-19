using System.Text.Json;
using PuppeteerSharp;

namespace PadreScraperApp;

public static class IndexedDbHelper
{
    /// <summary>
    /// Извлекает все данные из всех Object Stores указанной IndexedDB.
    /// </summary>
    public static async Task<JsonElement?> GetAllIndexedDbDataAsync(IPage page, string dbName)
    {
        return await page.EvaluateFunctionAsync<JsonElement?>(@"async (dbName) => {
            return new Promise((resolve, reject) => {
                const request = window.indexedDB.open(dbName);
                request.onerror = e => reject('DB Error: ' + e.target.errorCode);
                request.onsuccess = e => {
                    const db = e.target.result;
                    const storeNames = Array.from(db.objectStoreNames);
                    if (storeNames.length === 0) {
                        resolve({});
                        db.close();
                        return;
                    }
                    
                    // Важно: транзакцию нужно открывать после получения списка хранилищ
                    const transaction = db.transaction(storeNames, 'readonly');
                    const allData = {};
                    let storesCompleted = 0;

                    storeNames.forEach(storeName => {
                        const store = transaction.objectStore(storeName);
                        const getAllRequest = store.getAll();

                        getAllRequest.onsuccess = () => {
                            allData[storeName] = getAllRequest.result;
                            storesCompleted++;
                            if (storesCompleted === storeNames.length) {
                                resolve(allData);
                            }
                        };
                        getAllRequest.onerror = err => reject('Failed to getAll from ' + storeName);
                    });

                     transaction.oncomplete = () => db.close();
                     transaction.onerror = (e) => reject('Transaction error: ' + e.target.error);
                };
            });
        }", dbName);
    }

    /// <summary>
    /// Очищает и записывает данные в Object Stores указанной IndexedDB.
    /// </summary>
    public static async Task<bool> SetAllIndexedDbDataAsync(IPage page, string dbName, JsonElement data)
    {
        // Проверяем, что data - это объект (словарь)
        if (data.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine("SetAllIndexedDbDataAsync: Полученные данные IndexedDB не являются объектом.");
            return false;
        }

        return await page.EvaluateFunctionAsync<bool>(@"async (dbName, data) => {
            return new Promise((resolve, reject) => {
                const request = window.indexedDB.open(dbName);
                request.onerror = e => reject('DB Open Error: ' + e.target.errorCode);
                request.onsuccess = e => {
                    const db = e.target.result;
                    const storeNames = Object.keys(data);

                    if (storeNames.length === 0) {
                        resolve(true);
                        db.close();
                        return;
                    }

                    // Проверяем, все ли нужные хранилища существуют
                    const missingStores = storeNames.filter(name => !db.objectStoreNames.contains(name));
                    if (missingStores.length > 0) {
                         console.warn('Отсутствуют хранилища в целевой БД:', missingStores);
                         // Пытаемся продолжить с существующими
                    }

                    const storesToWrite = storeNames.filter(name => db.objectStoreNames.contains(name));

                    if(storesToWrite.length === 0)
                    {
                        resolve(true);
                        db.close();
                        return;
                    }

                    const transaction = db.transaction(storesToWrite, 'readwrite');

                    transaction.oncomplete = () => {
                        console.log('IndexedDB transaction complete.');
                        db.close();
                        resolve(true);
                    };
                    
                    transaction.onerror = (e) => {
                        console.error('Transaction error', e.target.error);
                        db.close();
                        reject('Transaction error: ' + e.target.error);
                    };

                    storesToWrite.forEach(storeName => {
                        try {
                            const store = transaction.objectStore(storeName);
                            store.clear(); // Очищаем перед записью

                            const items = data[storeName];
                            if (Array.isArray(items)) {
                                items.forEach(item => {
                                    try {
                                        store.put(item);
                                    } catch (putError) {
                                        console.error(`Ошибка при put в ${storeName}:`, putError, item);
                                    }
                                });
                            }
                        } catch (storeError) {
                             console.error(`Ошибка при доступе к хранилищу ${storeName}:`, storeError);
                        }

                    });
                };
            });
        }", dbName, data);
    }
}