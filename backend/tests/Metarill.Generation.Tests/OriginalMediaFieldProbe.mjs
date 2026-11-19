import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { mountGenerated, plain } from './GeneratedOriginalVueHarness.mjs'

const frontend = join(process.argv[2], 'frontend')
const source = await readFile(join(frontend, 'pages/yzp/customer-detail-101.vue'), 'utf8')
assert.ok(source.includes("return-type='object'"))
assert.ok(source.includes("file-mediatype='video' file-extname='mp4'"))
assert.ok(source.includes("file-mediatype='all'"))
assert.ok(source.includes("limit='9' v-model='entity.legacyGallery'"))
assert.ok(source.includes("class='base64img'"))
const form = mountGenerated(source, {}, {}, { runtimeDirectory: join(frontend, 'common/yzp_runtime') })
const uploads = []
form.yzp.uploadSelectedFiles = (event, options, finish) => uploads.push({ event, options, finish })
const file = (id, extname = 'png') => ({ url: '/api/admin/sys/file-library/public-content/' + id, name: id + '.' + extname, extname })
try {
 await form.flush()
 form.state.entity.name = '旧附件'
 const existing = file('11'), photo = file('12'), gallery = file('13'), attachment = file('14', 'pdf'), video = file('15', 'mp4')
 form.state.entity.photo = [existing]
 form.state.entity.gallery = [existing]
 const selected = { tempFiles: [{ path: 'isolated-fixture' }] }
 form.state.photoFileSelect(selected)
 assert.equal(uploads[0].options.cropImages, true)
 uploads[0].finish([photo])
 assert.deepEqual(plain(form.state.entity.photo), [photo])
 form.state.photoPickerChange(null)
 assert.equal(form.state.entity.photo, null)
 form.state.photoFileSelect(selected)
 uploads[1].finish([photo])
 form.state.galleryFileSelect(selected)
 uploads[2].finish([gallery])
 assert.equal(uploads[2].options.cropImages, true)
 assert.deepEqual(plain(form.state.entity.gallery), [existing, gallery])
 form.state.documentFileSelect(selected)
 uploads[3].finish([attachment])
 form.state.videoFileSelect(selected)
 uploads[4].finish([video])
 assert.equal(uploads[4].options.cropImages, false)

 await form.state.saveEntity()
 await form.flush()
 const write = form.writes().at(-1)
 assert.ok(write)
 assert.deepEqual(plain(write.options.data.photo), [photo])
 assert.deepEqual(plain(write.options.data.gallery), [existing, gallery])
 assert.deepEqual(plain(write.options.data.document), [attachment])
 assert.deepEqual(plain(write.options.data.video), [video])

 form.state.photoFileSelect(selected)
 form.state.photoFileSelect(selected)
 uploads[6].finish([photo])
 uploads[5].finish([file('16')])
 assert.deepEqual(plain(form.state.entity.photo), [photo])
 form.state.photoFileSelect(selected)
 form.setProps({ parentReadOnlyCondition: true })
 await form.flush()
 uploads[7].finish([file('17')])
 assert.deepEqual(plain(form.state.entity.photo), [photo])
} finally {
 form.app.unmount()
}
