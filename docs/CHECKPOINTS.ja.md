# 原本保全とcheckpoint

編集原本の `.blend` と生成スクリプト、配布用FBX/GLBを両方保持する。生成後に原本を消して容量を減らさない。GitHubへのpushだけを保全確認とせず、参照切れのないZIPをLibraryにも保存する。

1. 生成と静的テストを終了し、担当者全員のファイル書込みが終わったことを確認。
2. repoルートで `python tools/make_checkpoint.py` を実行。
3. ZIP内の全ファイルをSHA256照合し、`artifacts/drive-roadster-checkpoint.zip` のサイズ/ハッシュを記録。
4. 初回はLibraryに新規保存。次回は返された同じLibrary項目IDとバージョンを使って置換し、履歴を維持。
5. Library保存結果が確認できてからcheckpoint完了と報告する。ローカルZIPの生成だけではLibrary保存完了ではない。
6. GitHub push後は実際のremote commitを確認し、CIと未実施Unity試験を分けて報告。

秘密情報や認証情報は含めない。Libraryの保存ID等はcheckpoint管理情報として保持し、公開リポジトリへ認証情報を記録しない。
