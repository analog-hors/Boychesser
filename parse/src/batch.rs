type IndexFeaturePair = [i64; 2];

pub struct Batch {
    // The maximum number of entries
    capacity: usize,

    stm_feature_buffer: Box<[IndexFeaturePair]>,
    nstm_feature_buffer: Box<[IndexFeaturePair]>,
    values: Box<[f32]>,
    total_features: usize,

    cp: Box<[f32]>,
    wdl: Box<[f32]>,

    // The number of entries actually written
    entries: usize
}

impl Batch {
    pub fn new(capacity: usize, max_features: usize) -> Self {
        Self {
            capacity,
            stm_feature_buffer: vec![[0; 2]; capacity * max_features]
                .into_boxed_slice(),
            nstm_feature_buffer: vec![[0; 2]; capacity * max_features]
                .into_boxed_slice(),
            total_features: 0,
            values: vec![1.0; capacity * max_features].into_boxed_slice(),
            cp: vec![0_f32; capacity].into_boxed_slice(),
            wdl: vec![0_f32; capacity].into_boxed_slice(),
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
        self.total_features = 0;
    }

    pub fn capacity(&self) -> usize {
        self.capacity
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

    pub fn total_features(&self) -> usize {
        self.total_features
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
        self.batch.stm_feature_buffer[self.batch.total_features] = [self.index_in_batch as i64, stm_feature];
        self.batch.nstm_feature_buffer[self.batch.total_features] = [self.index_in_batch as i64, nstm_feature];
        self.batch.total_features += 1;
    }
}
