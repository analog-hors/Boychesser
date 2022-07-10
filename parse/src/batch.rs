type IndexFeaturePair = [i64; 2];

pub struct Batch {
    // The maximum number of entries
    batch_size: usize,

    stm_feature_buffer: Box<[IndexFeaturePair]>,
    nstm_feature_buffer: Box<[IndexFeaturePair]>,
    values: Box<[f32]>,
    feature_buffer_len: usize,

    cp: Box<[f32]>,
    wdl: Box<[f32]>,

    // The number of entries actually written
    entries: usize
}

impl Batch {
    pub fn new(batch_size: usize, max_features: usize) -> Self {
        Self {
            batch_size,
            stm_feature_buffer: vec![[0; 2]; batch_size * max_features]
                .into_boxed_slice(),
            nstm_feature_buffer: vec![[0; 2]; batch_size * max_features]
                .into_boxed_slice(),
            feature_buffer_len: 0,
            values: vec![1.0; batch_size * max_features].into_boxed_slice(),
            cp: vec![0_f32; batch_size].into_boxed_slice(),
            wdl: vec![0_f32; batch_size].into_boxed_slice(),
            entries: 0
        }
    }

    pub fn make_entry(&mut self, cp: f32, wdl: f32) -> EntryFeatureWriter {
        let index_in_batch = self.entries;
        self.entries += 1;
        self.cp[index_in_batch] = cp;
        self.wdl[index_in_batch] = wdl;
        EntryFeatureWriter {
            batch: self,
            index_in_batch
        }
    }

    pub fn clear(&mut self) {
        self.entries = 0;
        self.feature_buffer_len = 0;
    }

    pub fn batch_size(&self) -> usize {
        self.batch_size
    }

    pub fn len(&self) -> usize {
        self.entries
    }

    pub fn stm_feature_buffer_ptr(&self) -> *const i64 {
        &self.stm_feature_buffer[0][0]
    }

    pub fn nstm_feature_buffer_ptr(&self) -> *const i64 {
        &self.nstm_feature_buffer[0][0]
    }

    pub fn values_ptr(&self) -> *const f32 {
        &self.values[0]
    }

    pub fn feature_buffer_len(&self) -> usize {
        self.feature_buffer_len
    }

    pub fn cp_ptr(&self) -> *const f32 {
        &self.cp[0]
    }

    pub fn wdl_ptr(&self) -> *const f32 {
        &self.wdl[0]
    }
}

pub struct EntryFeatureWriter<'b> {
    batch: &'b mut Batch,
    index_in_batch: usize
}

impl EntryFeatureWriter<'_> {
    pub fn add_feature(&mut self, stm_feature: i64, nstm_feature: i64) {
        self.batch.stm_feature_buffer[self.batch.feature_buffer_len] = [self.index_in_batch as i64, stm_feature];
        self.batch.nstm_feature_buffer[self.batch.feature_buffer_len] = [self.index_in_batch as i64, nstm_feature];
        self.batch.feature_buffer_len += 1;
    }
}
